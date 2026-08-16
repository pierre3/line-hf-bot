# 仕様: text-to-video を fal-ai プロバイダ経由に対応（動画プロバイダ統合）

- 状態: ドラフト（仕様ゲート待ち）
- 対象: 既存の動画生成（`VideoService`）を、実際に動くプロバイダ（fal-ai）へ接続し `/video` を有効化可能にする
- 関連: `docs/specs/05-image-edit-fal-provider.md`（fal 非同期キュー方式の先行実装）、`docs/specs/02-image-provider-integration.md`（SSRF allowlist / `MediaRefetch`）
- 背景: 既定の `hf-inference` は **text-to-video を提供しておらず**、動画は既定オフ（`App__VideoEnabled=false`）のまま温存されていた。HF Inference Providers では text-to-video は **fal-ai / replicate / wavespeed** 等の GPU プロバイダが提供。fal-ai は image-to-image（spec05）と同じ**非同期キュー**方式。

## 1. 目的 / スコープ
`VideoService` を **fal-ai の非同期キュー方式**へ書き換え、`/video`（text-to-video）が実際に動くようにする。`IVideoService` の契約（`GenerateAsync(string prompt, ct)`）は不変＝`WorkProcessor`/配線（`WorkKind.Video`・`HandleVideoAsync`・`PushVideoAsync`・QuickReply）は無改修。

fal キューの共通処理（submit→poll→URL 書き換え）は spec05 の `HuggingFaceImageEditService` に private 実装済みのため、**共通ヘルパー `Ai/FalQueue.cs` に抽出**し、image-to-image（画像編集）と text-to-video の両サービスで再利用する（重複解消・トークン漏洩防止ロジックの一本化）。

### スコープ外
- **image-to-video**（画像→動画）。基盤（本 spec の fal キュー動画）を再利用する後続タスク。
- replicate / wavespeed 対応（別形式。v1 は fal-ai を既定に。operator が endpoint/model を差し替える余地は残す）。
- 動画生成パラメータ（解像度・長さ・fps 等）のユーザー指定。初版は prompt のみ。
- `App__VideoEnabled` の既定変更（**既定 OFF のまま**。理由は §4）。

## 2. 確定したワイヤ形式（huggingface_hub `FalAITextToVideoTask` 実装で確認・2026-08-16）
fal-ai text-to-video は image-to-image と同じ `FalAIQueueTask` 派生の**非同期キュー**（`src/huggingface_hub/inference/_providers/fal_ai.py`）。

1. **submit**: `POST https://router.huggingface.co/fal-ai/{providerModel}?_subdomain=queue`（`Authorization: Bearer hf_***`）
   - `{providerModel}` = `fal-ai/wan/v2.2-5b/text-to-video`（Wan2.2-5B の fal providerModelId）
   - body: `{ "prompt": <text> }`（＝`{"prompt": inputs, **filter_none(parameters)}`。初版は parameters なし）
   - 応答 200: `{ "status":"IN_QUEUE", "request_id", "status_url":"https://queue.fal.run/...", "response_url":"https://queue.fal.run/..." }`
2. **poll**: `status_url` を **router 書き換え**して GET（HF トークン付き）
   - 書き換え: 先頭 `https://queue.fal.run/` → `https://router.huggingface.co/fal-ai/`、末尾に `?_subdomain=queue` を付与
   - **queue.fal.run を直接叩くと 401**（fal は HF トークンを受け付けない）→ router 経由必須
   - `status` が `COMPLETED` まで（`IN_QUEUE`/`IN_PROGRESS` は継続、その他は失敗）
3. **result**: `response_url`（同書き換え）を GET → `{ "video": { "url":"https://<sub>.fal.media/...mp4", "content_type", ... }, "seed", ... }`
   - huggingface_hub は `output["video"]["url"]` を取得（＝**`video.url`**。image-to-image の `images[0].url` とはここだけ異なる）
4. **video**: `video.url` を **SSRF ガード付き再取得**（`MediaRefetch`、`fal.media` は allowlist 済。ラベル境界一致で `cdn.fal.media` 等も許可）→ bytes（`video/mp4`）

## 3. 実装方針
- **共通化**: `Ai/FalQueue.cs`（internal static）を新設し、以下を集約：
  - `SubmitAsync(http, submitUrl, body, apiKey, ct)` → submit（Bearer）→ `status_url`/`response_url` を **router 書き換えして返す**
  - `PollUntilCompletedAsync(http, statusUrl, apiKey, ct)` → ~1s 間隔で `COMPLETED` まで poll
  - `GetResultAsync(http, responseUrl, apiKey, ct)` → 結果 JSON（`JsonDocument`）を返し、**URL 抽出は各サービスが担当**（image=`images[0].url` / video=`video.url`）
  - `ToRouterUrl(url)`（internal static）→ `queue.fal.run` 始まりのみ受理・router 書き換え（トークン漏洩防止）。spec05 の実装をそのまま移設
- `HuggingFaceImageEditService` を `FalQueue` 利用へリファクタ（挙動不変。private の submit/poll/ToRouterUrl を削除し共通版に委譲）。
- `HuggingFaceVideoService.GenerateAsync` を fal キュー方式へ書き換え（submit `{prompt}`→poll→`video.url` 抽出→`MediaRefetch`）。旧「バイト or JSON-URL 同期」パス（`MediaResponse.ReadAsync` 経由）は video では廃止。
- **トークン漏洩防止**: `status_url`/`response_url` は **`https://queue.fal.run/` 始まりのみ受理**し router 書き換え後のみ HF トークンを付けて GET（＝HF トークンは `router.huggingface.co` にのみ送る）。想定外ホストは例外。最終動画取得は `MediaRefetch`（https 限定・allowlist・**Authorization なし**・timeout）。
- ポーリング/取得の全体は `VideoTimeoutSeconds`（既定 300）を linked CTS で打ち切り（既存 OCE 経路）。
- 非2xx は既存 `HfHttp.EnsureSuccessAsync`（本文つき例外）で surface＝ログに fal のエラー本文が残る。ユーザーには既存の汎用エラー文言。
- `Program.cs` の `IVideoService` typed client は既に `AllowAutoRedirect=false`（リダイレクトによる allowlist 迂回対策）。追加変更不要。dev 検証用 `/dev/video` は既存（無改修）。

## 4. 設定変更（既定）
| キー | 変更前 | 変更後 |
|---|---|---|
| `HuggingFace__VideoModel` | `Wan-AI/Wan2.2-TI2V-5B` | `fal-ai/wan/v2.2-5b/text-to-video`（fal providerModelId） |
| `HuggingFace__VideoEndpoint` | `https://router.huggingface.co/hf-inference/models/{model}` | `https://router.huggingface.co/fal-ai/{model}?_subdomain=queue` |

- `VideoTimeoutSeconds`(300) 据え置き。`appsettings.json` / `.env.example` / README(EN/JA) / CLAUDE.md 反映。
- **`App__VideoEnabled` は既定 `false` を維持**（決定）。理由: fal 動画は**有料かつ生成が重い/遅い**ため、個人・小規模利用で不意の課金を避け opt-in とする。operator が `App__VideoEnabled=true` で有効化。README/CLAUDE に有効化手順と有料注記を明記。
- ※ **fal は有料プロバイダ**。HF トークンに Inference Providers 権限＋クレジット/課金が必要（image-edit と同条件）。

## 5. 受入基準（テスト可能）
1. submit は `{prompt}` を fal エンドポイント（`https://router.huggingface.co/fal-ai/fal-ai/wan/v2.2-5b/text-to-video?_subdomain=queue`）へ POST（Bearer 付き）。
2. `status_url`/`response_url` は router 書き換え（`queue.fal.run`→`router.huggingface.co/fal-ai/`＋`_subdomain=queue`）して GET され、HF トークンは router 以外に送られない。
3. `status=COMPLETED` まで poll → `response_url` から `video.url` を取得。
4. `video.url`（fal.media）は SSRF allowlist 経由・**Authorization なし**で再取得し、`video/mp4` として返す。
5. allowlist 外ホストの結果 URL は拒否（例外）。
6. `status_url`/`response_url` が `queue.fal.run` 以外を指す場合は拒否（トークン漏洩防止）。
7. タイムアウトは `VideoTimeoutSeconds` で打ち切り（既存 OCE 経路）。
8. 既定値（VideoModel=`fal-ai/wan/v2.2-5b/text-to-video` / VideoEndpoint=fal router テンプレート）がドキュメント・`appsettings.json` と一致。
9. `FalQueue.ToRouterUrl` は `queue.fal.run` 始まりのみ書き換え、他ホスト/http は拒否（image-edit と共有・回帰なし）。
10. 既存 `ImageEditServiceTests` は `FalQueue` 抽出後も緑（画像編集の挙動不変）。旧「同期 video JSON-URL」テスト（`MediaServiceTests`）は fal キュー方式へ置換。

## 6. 決定事項（2026-08-16）
- [x] text-to-video は **fal-ai** を既定プロバイダに（Wan2.2-5B。hf-inference 非対応）。ワイヤ形式は huggingface_hub 実装で確認（推測せず）。
- [x] 既定モデル = **Wan2.2-5B**（HF 推奨・5B で比較的安価/高速）。operator が env で差し替え可能。
- [x] fal キュー共通処理を `Ai/FalQueue.cs` に抽出し image-edit と共有。
- [x] トークンは `queue.fal.run`→`router.huggingface.co` 書き換え後のみ送信。最終動画は fal.media を no-auth + allowlist 取得。
- [x] `App__VideoEnabled` は**既定 OFF 維持**（fal 有料・重い。opt-in）。
- [x] 検証は**ユニットテスト + 4ゲート**（実機 fal E2E は課金回避のため operator に委ねる。ワイヤ形式は上流実装で確定済み）。
- [x] scope: text-to-video のみ。image-to-video は後続。

## 7. 参考
- huggingface_hub `_providers/fal_ai.py`: `FalAITextToVideoTask`（body `{"prompt": inputs, **params}`、result `output["video"]["url"]`）／`FalAIQueueTask`（submit→poll status_url→response_url、`_subdomain=queue`）。
- HF text-to-video タスク: providersMapping fal-ai=`fal-ai/wan/v2.2-5b/text-to-video`（modelId `Wan-AI/Wan2.2-TI2V-5B`）。
- `Ai/MediaRefetch.cs`（SSRF）/ `Ai/HfHttp.cs`（EnsureSuccess＋本文）。
- `docs/specs/05-image-edit-fal-provider.md`（fal キュー先行実装・共通化の元）。
