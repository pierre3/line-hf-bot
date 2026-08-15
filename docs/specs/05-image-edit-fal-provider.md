# 仕様: 画像編集（image-to-image）を fal-ai プロバイダ経由に対応

- 状態: 実装済み・実機E2E検証済み（`/dev/imageedit` で SD3生成→fal編集→有効PNG 1.45MB を確認）。3ゲート全PASS（実装=30 / セキュリティ=31 / ドキュメント=32）
- 対象: 既存 3b の画像編集（`ImageEditService`）を、実際に動くプロバイダ（fal-ai）へ接続
- 関連: `docs/specs/03-mode-context-richmenu.md`（3b ✏️編集）、`docs/specs/04-user-image-edit.md`（受信写真→編集）、`docs/specs/02-image-provider-integration.md`（SSRF allowlist / MediaRefetch 流用）
- 背景: 既定の `hf-inference` は **image-to-image を提供しておらず**、`Qwen/Qwen-Image-Edit` 呼び出しが `HF 400 {"error":"Model not supported by provider hf-inference"}` で失敗（実機ログで確認）。HF Inference Providers では image-to-image は **fal-ai / replicate / wavespeed** が提供。

## 1. 目的 / スコープ
✏️編集（生成画像の編集）と spec04（受信写真の編集）が実際に動くよう、`ImageEditService` を **fal-ai の非同期キュー方式**に対応させる。`IImageEditService` の契約（`GenerateAsync(byte[] ref, string instruction, ct)`）は不変＝`WorkProcessor`/呼び出し側は無改修。

### スコープ外
- replicate / wavespeed 対応（別形式。v1 は fal-ai を既定に。operator が endpoint/model を差し替える余地は残す）。
- image→video、動画プロバイダ統合。

## 2. 確定したワイヤ形式（実機検証済み・2026-08-15）
1. **submit**: `POST https://router.huggingface.co/fal-ai/{providerModel}?_subdomain=queue`（`Authorization: Bearer hf_***`）
   - `{providerModel}` = `fal-ai/qwen-image-edit`
   - body: `{ "prompt": <instruction>, "image_url": "data:image/png;base64,<ref>", "image_urls": ["data:..."] }`（入力画像は base64 データURI）
   - 応答 200: `{ "status":"IN_QUEUE", "request_id", "status_url":"https://queue.fal.run/...", "response_url":"https://queue.fal.run/..." }`
2. **poll**: `status_url` を **router 書き換え**して GET（HF トークン付き）
   - 書き換え: 先頭 `https://queue.fal.run/` → `https://router.huggingface.co/fal-ai/`、末尾に `?_subdomain=queue` を付与
   - **queue.fal.run を直接叩くと 401**（fal は HF トークンを受け付けない）→ router 経由必須
   - `status` が `COMPLETED` まで（`IN_QUEUE`/`IN_PROGRESS` は継続、その他は失敗）
3. **result**: `response_url`（同書き換え）を GET → `{ "images":[{ "url":"https://<sub>.fal.media/...png", "width","height","content_type" }], ... }`
4. **image**: `images[0].url` を **SSRF ガード付き再取得**（`MediaRefetch`、`fal.media` は allowlist 済。`v3b.fal.media` 等はラベル境界一致で許可）→ bytes
- 制約: 入力画像は **最小 256×256**（未満は fal が 422 `image_too_small`）。

## 3. 実装方針
- `HuggingFaceImageEditService.GenerateAsync` を fal キュー方式に書き換え（submit→poll→result→refetch）。
- **トークン漏洩防止**: `status_url`/`response_url` は **`https://queue.fal.run/` 始まりのみ受理**し、router へ書き換えてから HF トークンを付けて GET する（＝HF トークンは `router.huggingface.co` にのみ送る。任意ホストへは送らない）。想定外ホストは例外。
- ポーリング間隔 ~1s、全体は `ImageEditTimeoutSeconds`（既定 120）を linked CTS で打ち切り。
- 非2xx は既存 `HfHttp.EnsureSuccessAsync`（本文つき例外）で surface＝ログに fal のエラー本文（例 `image_too_small`）が残る。ユーザーには既存の汎用エラー文言。
- 最終画像取得は `MediaRefetch`（https 限定・allowlist・no-auth・timeout）を流用。JSON パースは fal 形式（`images[0].url`）を service 内で明示。

## 4. 設定変更（既定）
| キー | 変更前 | 変更後 |
|---|---|---|
| `HuggingFace__ImageEditModel` | `Qwen/Qwen-Image-Edit` | `fal-ai/qwen-image-edit`（fal のプロバイダモデルID） |
| `HuggingFace__ImageEditEndpoint` | `https://router.huggingface.co/hf-inference/models/{model}` | `https://router.huggingface.co/fal-ai/{model}?_subdomain=queue` |

`ImageEditTimeoutSeconds`(120) 据え置き。`.env.example` / README(EN/JA) / CLAUDE.md 反映。
※ **fal は有料プロバイダ**。HF トークンに Inference Providers 権限＋クレジット/課金が必要（README/CLAUDE に注記）。

## 5. 受入基準（テスト可能）
1. submit は `{prompt, image_url(dataURI), image_urls}` を fal エンドポイントへ POST（Bearer 付き）。
2. `status_url`/`response_url` は router 書き換え（queue.fal.run→router.huggingface.co/fal-ai/＋`_subdomain=queue`）して GET され、HF トークンは router 以外に送られない。
3. `status=COMPLETED` まで poll → `response_url` から `images[0].url` を取得。
4. `images[0].url`（fal.media）は SSRF allowlist 経由・**Authorization なし**で再取得。
5. allowlist 外ホストの結果 URL は拒否（例外）。
6. `status_url`/`response_url` が `queue.fal.run` 以外を指す場合は拒否（トークン漏洩防止）。
7. タイムアウトは `ImageEditTimeoutSeconds` で打ち切り（既存 OCE 経路）。
8. 既定値（ImageEditModel=`fal-ai/qwen-image-edit` / ImageEditEndpoint=fal router テンプレート）がドキュメントと一致。
9. 既存テスト回帰なし（`ImageEditServiceTests` は fal 方式へ書き換え）。

## 6. 決定事項（2026-08-15）
- [x] 画像編集は **fal-ai** を既定プロバイダに（Qwen-Image-Edit は fal/replicate/wavespeed 提供、hf-inference 非対応）。
- [x] 非同期キュー（submit→poll→result）＋ router 書き換えで HF トークン認証。
- [x] トークンは `queue.fal.run`→`router.huggingface.co` 書き換え後のみ送信（漏洩防止）。最終画像は fal.media を no-auth + allowlist 取得。
- [x] `IImageEditService` 契約不変＝上位（受信写真編集/✏️編集）は無改修で両方が動く。
- [x] fal 有料の注記をドキュメントに追加。

## 7. 参考
- `Ai/MediaRefetch.cs`（SSRF）/ `Ai/HfHttp.cs`（EnsureSuccess＋本文）/ `Ai/MediaResponse.cs`。
- huggingface_hub `_providers/fal_ai.py`（queue task）・`_common.py`（`INFERENCE_PROXY_TEMPLATE=https://router.huggingface.co/{provider}`）。
- HF: `Qwen/Qwen-Image-Edit` inferenceProviderMapping（fal-ai=`fal-ai/qwen-image-edit`）。
