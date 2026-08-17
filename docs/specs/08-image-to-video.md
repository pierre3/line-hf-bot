# 仕様: image-to-video（写真/生成画像を fal-ai で動画化）

- 状態: ドラフト（仕様ゲート待ち）
- 対象: 既存の画像（生成画像・編集結果・ユーザー送信写真）を入力に、fal-ai の image-to-video で短い動画を生成する
- 関連: `docs/specs/06-video-fal-provider.md`（text-to-video の fal 非同期キュー＝基盤）、`docs/specs/05-image-edit-fal-provider.md`（参照画像を data URI で fal に渡す方式）、`docs/specs/04-user-image-edit.md`（受信写真の取得・保存・保留アクション）、`docs/specs/07-image-vision-vqa.md`（受信写真の選択 QuickReply／`PendingAction`）
- 背景: spec06 で text-to-video の fal 非同期キュー（`Ai/FalQueue.cs`）が入り、spec05 で参照画像を base64 data URI として fal に送る image-to-image が入った。image-to-video は**この2つの合わせ技**で、新規のプロトコル面はほぼ無い（body に `image_url` を足し、結果は t2v と同じ `video.url`）。

## 1. 目的 / スコープ
ユーザーの**作業中画像**（直近の生成/編集結果、または送信写真）を入力に、モーション指示（prompt）を添えて **image-to-video** を実行し、動画を返す。トリガーは結果／写真受信時の **QuickReply ボタン「🎬 動画にする」**。タップ後の**次の非コマンドテキストをモーション指示**として処理する（既存の編集フローと同じ `PendingAction` 機構を流用）。

fal キューの共通処理（submit→poll→URL 書き換え→SSRF 再取得）は `Ai/FalQueue.cs` に実装済みのため**再利用**する。image-to-video 固有なのは「submit body に `image_url`（参照画像の data URI）を含める」点と「結果から `video.url` を抽出」点のみ（後者は t2v と同一）。

### スコープ外
- 動画パラメータ（解像度・長さ・fps・seed 等）のユーザー指定。初版は「参照画像 + モーション prompt」のみ。
- prompt 無し（ワンタップ即生成）UX。**決定: タップ→モーション指示入力に統一**（§6）。
- text-to-video の既定変更や replicate/wavespeed 対応（別プロバイダ）。operator が endpoint/model を差し替える余地は残す。
- Vision 無効（`App__VisionEnabled=false`）時に**受信写真から**動画化する導線。受信写真の Animate は選択 QuickReply（Vision 有効時のみ表示）に相乗りするため（§3.2）。生成画像からの動画化は Vision 設定に依らず可能。

## 2. 確定したワイヤ形式（huggingface_hub `FalAIImageToVideoTask` 実装で確認・2026-08-17）
image-to-video は t2v/image-edit と同じ `FalAIQueueTask` 派生の**非同期キュー**（`src/huggingface_hub/inference/_providers/fal_ai.py`）。

```python
class FalAIImageToVideoTask(FalAIQueueTask):
    def __init__(self):
        super().__init__("image-to-video")
    def _prepare_payload_as_dict(self, inputs, parameters, provider_mapping_info):
        image_url = _as_url(inputs, default_mime_type="image/jpeg")
        payload = { "image_url": image_url, **filter_none(parameters) }
        ...
        return payload
    def get_response(self, response, request_params=None):
        output = super().get_response(response, request_params)
        url = _as_dict(output)["video"]["url"]
        return get_session().get(url).content
```

1. **submit**: `POST https://router.huggingface.co/fal-ai/{providerModel}?_subdomain=queue`（`Authorization: Bearer hf_***`）
   - `{providerModel}` = `fal-ai/wan/v2.2-a14b/image-to-video`（Wan-AI/Wan2.2-I2V-A14B の fal providerModelId。HF providersMapping で `image-to-video` / status=live を確認）
   - body: `{ "image_url": "data:image/<mime>;base64,....", "prompt": <motion text>, "aspect_ratio": "16:9"|"9:16"|"1:1" }`
     - `image_url` = 参照画像の base64 data URI（画像は自前ホスト不要。spec05 と同じ手法）
     - `prompt` は `filter_none(parameters)` でマージされる**任意**キー。本 spec の UX ではユーザーがモーション指示を必ず入力するため常に付与する。
     - `aspect_ratio` = **必須（実機で判明）**。既定の `auto` は入力画像から出力サイズを解決するが、Wan2.2-A14B の distributed GPU エンドポイントは離散サイズしか受け付けず、多くの写真サイズで **result 取得時に 422**（例: 縦長 816×1104 → `The resolved output size 816x1104 is not supported ... Use aspect_ratio='16:9', '9:16', or '1:1'`）。→ **入力画像の寸法から最も近い対応比（16:9 / 9:16 / 1:1）を計算して明示送信**（縦長=9:16 / 横長=16:9 / 正方形=1:1）。寸法不明時は安全側で `1:1`。ハードコードしないのは逆向き画像の歪み回避のため。
   - 応答 200: `{ "status":"IN_QUEUE", "request_id", "status_url":"https://queue.fal.run/...", "response_url":"https://queue.fal.run/..." }`
2. **poll**: `status_url` を router 書き換え（`queue.fal.run/`→`router.huggingface.co/fal-ai/`＋`?_subdomain=queue`）して GET（HF トークン付き）。`COMPLETED` まで（`IN_QUEUE`/`IN_PROGRESS` は継続）。→ `FalQueue.PollUntilCompletedAsync` をそのまま使用。
3. **result**: `response_url`（同書き換え）を GET → `{ "video": { "url":"https://<sub>.fal.media/...mp4", ... }, ... }`。**`video.url`** を抽出（t2v と同一）。
4. **video**: `video.url`（fal.media）を **SSRF ガード付き再取得**（`MediaRefetch`、`fal.media` allowlist 済・**Authorization なし**）→ bytes（`video/mp4`）。

※ URL 書き換え・トークン漏洩防止・allowlist は spec06 と完全に同一経路（`FalQueue.ToRouterUrl` / `MediaRefetch`）。**新規 SSRF 面は無い**。

## 3. 実装方針

### 3.1 サービス（新規 `Ai/ImageToVideoService.cs`）
```csharp
public interface IImageToVideoService
{
    Task<GeneratedMedia> GenerateAsync(byte[] referenceImage, string referenceContentType, string prompt, CancellationToken ct);
}
```
- 実装 `HuggingFaceImageToVideoService(HttpClient http, IOptions<HuggingFaceOptions> options)`。
- タイムアウトは **`VideoTimeoutSeconds`（既定 300）を流用**（動画生成＝t2v と同等の所要時間のため、設定項目を増やさない）。linked CTS で打ち切り（既存 OCE 経路）。
- submit body = `new { image_url = dataUri, prompt, aspect_ratio }`。`dataUri = $"data:{mime};base64,{Convert.ToBase64String(referenceImage)}"`。`mime` は `referenceContentType`（空なら `image/png` にフォールバック）。`aspect_ratio` は新設ヘルパー `Ai/ImageDimensions.cs`（PNG/JPEG ヘッダのみ読む・依存追加なし＝chiseled ランタイムでも可）で入力寸法を取り、対応3比のうち log 比距離が最小のものを選ぶ（寸法不明→`1:1`）。
- `FalQueue.SubmitAsync` → `PollUntilCompletedAsync` → `GetResultAsync` を使用し、結果から `video.url` を抽出（`VideoService.ExtractVideoUrl` と同形の小さな private ヘルパー。spec06 の「抽出はサービス固有」方針を踏襲し重複を許容）。
- `MediaRefetch.FetchAsync` で最終取得、`video/mp4`（空なら補完）で返す。

### 3.2 トリガー / 配線
- **`WorkKind.ImageToVideo`** を追加（`Queue/WorkItem.cs`）。`Text`=モーション指示、`RefImageId`=参照画像の media id（＝作業中画像）。
- **`PendingAction.Animate`** を追加（`State/UserStateStore.cs`）。「次の非コマンドテキストを image-to-video のモーション指示として解釈」。既存のワンショット排他（mode 切替/スラッシュ/再生成で解除）に同居。
- **`MessageDispatcher`**:
  - 保留解決ブロック: `pendingKind` を switch 化（`VisionQuestion`→`Vision` / `Animate`→`ImageToVideo` / それ以外→`ImageEdit`）。`LastImageId` を `RefImageId` に載せる。画像が無ければ既存の `EditNoImage`。
  - postback `action=animate`: `edit`/`ask` と同型。`LastImageId` があれば `Pending=Animate` にして `AnimatePrompt` を返信。無ければ `EditNoImage`。
- **`QuickReplyFactory`**（`IOptions<AppOptions>` を注入）:
  - `ImageResult`（生成/編集結果）に、`VideoEnabled=true` のとき `Item(LabelAnimate, "action=animate")` を追加（Regenerate / Edit / **Animate** / Chat）。
  - `ReceivedImageChoices`（受信写真・Vision 有効時のみ使用）に、`VideoEnabled=true` のとき Animate を追加（Edit / Ask / **Animate**）。
  - `VideoResult` は変更なし（動画結果は Chat のみ）。
- **`WorkProcessor`**（`IImageToVideoService` を注入）:
  - `case WorkKind.ImageToVideo`: `VideoEnabled` が false なら `NotYetImplemented` を返す（t2v と同一の gate）。true なら `HandleImageToVideoAsync`。
  - `HandleImageToVideoAsync`: `PrepareMediaAsync(item, AnimatePrompt, GeneratingVideo, ct)` で（冪等化・prompt 空チェック・PublicBaseUrl・生成中 ack）を共通処理 → `RefImageId` を `mediaStore.TryGet`（期限切れは `EditImageExpired`）→ `imageToVideoService.GenerateAsync(bytes, contentType, prompt, ct)` → `mediaStore.Save` → `PushVideoAsync`（`VideoResult` QuickReply、プレビューは `VideoPreview.Path`）。

### 3.3 設定（`Configuration/BotOptions.cs`・`HuggingFaceOptions`）
- `ImageToVideoModel`（既定 `fal-ai/wan/v2.2-a14b/image-to-video`）。
- `ImageToVideoEndpoint`（既定 `https://router.huggingface.co/fal-ai/{model}?_subdomain=queue`。`{model}` 置換。t2v/image-edit と同型テンプレート）。
- タイムアウトは `VideoTimeoutSeconds` 流用（新設しない）。`MediaRefetchAllowedHosts` / `ApiKey` は共通。

### 3.4 文言（`Text/UserMessages.cs`、en/ja）
- `LabelAnimate`（en: `🎬 Make a video` / ja: `🎬 動画にする`）。
- `AnimatePrompt`（en: `How should it move? Send a short description. e.g. slowly zoom in` / ja: `どんな動きにしますか？ 短い説明を送ってください。例: ゆっくりズームイン`）。
- `Help` に1行追記（写真を「編集・質問・動画化」できる旨）。
- 期限切れは `EditImageExpired` を流用（編集と同じく生成/受信の双方をカバー）。生成中 ack は `GeneratingVideo` を流用。

### 3.5 DI / dev エンドポイント（`Program.cs`）
- `AddHttpClient<IImageToVideoService, HuggingFaceImageToVideoService>` を登録（`AllowAutoRedirect=false`。他 fal サービスと同じくリダイレクト allowlist 迂回対策）。
- Development のみ `/dev/imagetovideo?prompt=...`: text-to-image で参照画像を作り、それを i2v へ。mp4 か error 文字列を返す（`/dev/imageedit` と同型。実機写真なしで E2E 確認可能）。

## 4. 設定変更（既定・ドキュメント反映先）
| キー | 既定値 |
|---|---|
| `HuggingFace__ImageToVideoModel` | `fal-ai/wan/v2.2-a14b/image-to-video` |
| `HuggingFace__ImageToVideoEndpoint` | `https://router.huggingface.co/fal-ai/{model}?_subdomain=queue` |

- 反映先: `Configuration/BotOptions.cs`（コード既定）/ `appsettings.json` / `.env.example` / `README.md` / `README.ja.md` / `CLAUDE.md`（コード既定＝表＝appsettings.json の三者一致）。
- **有効化は `App__VideoEnabled` を流用**（既定 OFF 維持）。OFF 時は Animate ボタンを出さず、万一 postback が来ても `NotYetImplemented`。
- ※ **fal は有料・クレジット消費が重い**（image-edit / t2v と同条件）。A14B は t2v 既定の 5B より大きく単価は高め。より軽い代替 pin 例＝`fal-ai/wan-i2v`（Wan2.1）。README/CLAUDE に有料注記＋差し替え例を明記。

## 5. 受入基準（テスト可能）
1. submit は `{ image_url: <data URI>, prompt: <motion>, aspect_ratio: <16:9|9:16|1:1> }` を i2v エンドポイント（`https://router.huggingface.co/fal-ai/fal-ai/wan/v2.2-a14b/image-to-video?_subdomain=queue`）へ POST（Bearer 付き）。`image_url` は参照画像の base64 data URI。`aspect_ratio` は入力寸法から選択（縦長→9:16／横長→16:9／正方形・不明→1:1）。
2. `status_url`/`response_url` は router 書き換え（`queue.fal.run`→`router.huggingface.co/fal-ai/`＋`_subdomain=queue`）して GET され、HF トークンは router 以外に送られない。
3. `status=COMPLETED` まで poll → `response_url` から **`video.url`** を取得。
4. `video.url`（fal.media）は SSRF allowlist 経由・**Authorization なし**で再取得し、`video/mp4` として返す。
5. allowlist 外ホストの結果 URL は拒否（例外）。
6. `status_url`/`response_url` が `queue.fal.run` 以外を指す場合は拒否（トークン漏洩防止。`FalQueue` 共有ロジックの回帰なし）。
7. タイムアウトは `VideoTimeoutSeconds` で打ち切り（既存 OCE 経路）。
8. 既定値（ImageToVideoModel / ImageToVideoEndpoint）がドキュメント・`appsettings.json` と一致。
9. 配線: 「🎬 動画にする」postback → `Pending=Animate` → 次の非コマンドテキストが `WorkKind.ImageToVideo`（`RefImageId=LastImageId`）として enqueue。mode 切替/スラッシュ/再生成で Animate はキャンセル。
10. gate: `App__VideoEnabled=false` のとき Animate ボタンは QuickReply に含まれず、`WorkKind.ImageToVideo` は `NotYetImplemented` を返す。
11. QuickReplyFactory: `VideoEnabled=true` で `ImageResult` に Animate を含む（Regenerate/Edit/Animate/Chat）。`ReceivedImageChoices` に Animate を含む（Edit/Ask/Animate）。`false` で従来通り。
12. 既存テスト（`ImageEditServiceTests`・fal 動画・dispatcher・quickreply）は緑（回帰なし）。

## 6. 決定事項（2026-08-17）
- [x] トリガー UX = **タップ→モーション指示入力**（`PendingAction.Animate`、Edit フロー流用）。ワンタップ即生成は不採用（制御性・一貫性優先）。
- [x] 有効化フラグ = **`App__VideoEnabled` 流用**（新設しない）。既定 OFF 維持（fal 有料・重い）。
- [x] 既定モデル = **`fal-ai/wan/v2.2-a14b/image-to-video`**（Wan2.2・HF live 確認）。operator が env で差し替え可（例 `fal-ai/wan-i2v`）。
- [x] タイムアウトは **`VideoTimeoutSeconds` 流用**（設定項目を増やさない）。
- [x] 参照画像の mime は `GeneratedMedia.ContentType` を使用（空なら `image/png`）。data URI で送信＝自前ホスト不要。
- [x] 受信写真からの Animate は選択 QuickReply（Vision 有効時）に相乗り。生成画像からは Vision 設定に依らず可能。
- [x] 検証 = **ユニットテスト + 4ゲート**。dev 用 `/dev/imagetovideo` を用意。**実機 LINE E2E 成功（2026-08-17）**＝下記 `aspect_ratio` 対応後に縦長写真から動画生成を確認。
- [x] **`aspect_ratio` を必須送信（2026-08-17 実機で判明・追加）**: 既定 `auto` は A14B の distributed GPU で 422。入力寸法から対応3比（16:9/9:16/1:1）の最近傍を計算して送る（`Ai/ImageDimensions.cs`）。ハードコードは逆向き画像を歪めるため不採用。モデル差し替え時は param 名/受理値が変わりうる（operator 対応）。

## 7. 参考
- huggingface_hub `_providers/fal_ai.py`: `FalAIImageToVideoTask`（body `{"image_url": _as_url(inputs), **params}`、result `output["video"]["url"]`）／`FalAIQueueTask`（submit→poll status_url→response_url、`_subdomain=queue`）。
- HF providersMapping（`pipeline_tag=image-to-video`, provider=`fal-ai`）: `Wan-AI/Wan2.2-I2V-A14B` → `fal-ai/wan/v2.2-a14b/image-to-video`（status=live）。代替: `Wan-AI/Wan2.1-I2V-14B-720P` → `fal-ai/wan-i2v`。
- `Ai/FalQueue.cs`（submit/poll/router 書き換え）/ `Ai/MediaRefetch.cs`（SSRF）/ `Ai/HfHttp.cs`（EnsureSuccess＋本文）。
- `docs/specs/06-video-fal-provider.md`（t2v fal キュー＝基盤）/ `docs/specs/05-image-edit-fal-provider.md`（参照画像 data URI 送信）。
