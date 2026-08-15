# 仕様: ユーザー画像の受信 → 画像編集（image-to-image）

- 状態: 実装済み（仕様ゲート PASS=記録 `docs/reviews/25`。実装/セキュリティ/ドキュメントゲート予定）。テスト53件緑（既存45＋新規8）
- 対象: 拡張フェーズ / ユーザーが送った写真を編集の入力にする（受信画像 → ✏️編集フローへ接続）
- 関連: `docs/specs/03-mode-context-richmenu.md`（3b ✏️編集＝image-to-image を再利用）、`docs/specs/02-image-provider-integration.md`（応答分岐は編集側で既に利用済み）
- 依存: **spec 02 / spec 03(3a・3b) 実装済み（main マージ済み）を前提**。本仕様は 3b の編集フローに「入力画像の入手経路」を足すもの。

## 1. 目的 / スコープ
現状の ✏️編集（3b）は **ボットが生成した画像**（`LastImageId`）しか編集できない。
本仕様で **ユーザーが LINE で送った写真** を編集の入力にできるようにする。
受信画像を LINE Content API で取得 → 既存 `MediaStore` に保存 → `LastImageId` として扱い、
そのまま **image-to-image（`Qwen/Qwen-Image-Edit`）** の編集フローに載せる。

受信時 UX は **即編集プロンプト（Option A）**: 画像を受け取ったら即「どう編集しますか？」と返し、
`AwaitingEdit=true` にする。次の非コマンドテキストを編集指示として処理する。

### スコープ外
- **vision チャット（画像について質問＝VQA / 画像理解）は対象外**。受信画像は**編集専用**。
  画像理解は VLM モデル・マルチモーダル chat パスが要るため、別の後続 spec（05 予定）とする。
- **image→video（画像から動画生成）は対象外**（動画プロバイダ統合ごと保留）。
- 外部提供画像（`contentProvider.type=external`）の**外部URL自前取得は行わない**（SSRF 回避）。
- 複数画像の一括受信・アルバム・画像以外のファイル（動画/音声/ファイル）受信。v1 は**単一の画像メッセージのみ**。

## 2. 機能要件

### 2.1 画像メッセージの受信（Dispatcher）
- `MessageDispatcher.DispatchAsync` に **`MessageEvent { Message: ImageMessageContent img }`** の分岐を追加（現状は無視）。
- 取得元判定: `img.ContentProvider?.Type`（**列挙型 `ContentProvider_type`**、文字列ではない）。
  - `ContentProvider_type.Line` または未設定(null) → LINE が保管する本体を Content API（blob）で取得（§2.2）。
  - `ContentProvider_type.External` → **取得しない**。`messages.ImageSourceUnsupported` を返して終了（状態変更なし）。
- `img.Id`（LINE messageId）を持つ **`WorkItem(WorkKind.ReceiveImage)`** をキューに積む。webhook は従来どおり**即200**、取得は worker が実行。
  - `WorkItem` は LINE messageId を運ぶ。`Text` フィールドに messageId を載せる（`ImageEdit` が `Text`=編集指示を運ぶのと同じく、kind 依存で意味が変わる方式）。**`WorkItem` の XML doc に「ReceiveImage=LINE messageId」を追記**する。
- **モード非依存**: どのモード（chat/image/video）でも画像受信は編集フローに入る（画像＝編集意図が明確なため）。
- `AwaitingEdit` 中に新たに画像を受信した場合も同じ経路（worker が新画像で `LastImageId` を上書きし再プロンプト）。

### 2.2 LINE Content API での本体取得（新規サービス）
- **新規 `ILineContentService` / `LineContentService`**。**DI 登録済みの `MessagingClient` を注入**（既存 `LineMessenger` と同型）。`AddLineMessaging` が登録するのは facade の `MessagingClient` のみで、**`MessagingBlobApiClient` は DI 未登録＝直接注入しない**。取得は `MessagingClient` のデータプレーン facade `client.Blob` から行う。
  - 取得: `client.Blob.V2.Bot.Message[messageId].Content.GetAsync(ct)` → `Stream`（データプレーン host `api-data.line.me` は SDK が処理。制御プレーンの `client.Api...`＝`api.line.me` とは別系統。`.Api.V2` は誤り）。
  - `Task<GeneratedMedia> FetchAsync(string messageId, CancellationToken ct)` を公開。戻りは bytes ＋ contentType。
- **サイズ上限**: ストリームを**上限付き**で読み込む。上限超過は例外（or 失敗結果）にし、worker が `messages.ImageTooLarge` を返す。既定 10MB（`Line__MaxIncomingImageBytes`）。
  - 上限読み取りロジックは**テスト可能なヘルパー**（例 `ReadCappedAsync(Stream, maxBytes)`）に切り出す。
- **contentType**: blob 応答のヘッダが取れない場合は `image/jpeg` を既定にする（受信画像は編集の base64 入力にするだけで、`/media/{id}` 再配信はしないため厳密不要）。
- **タイムアウト**: `CancellationTokenSource` で取得にタイムアウトを掛ける（既存 HF サービスと同様）。既定 30 秒（`Line__ContentFetchTimeoutSeconds`）。
- **SSRF/リダイレクト**: 取得先はホスト固定（`api-data.line.me`、SDK 管理）で任意 URL を叩かないため SSRF 面なし。外部URL取得はしない（§2.1）。

### 2.3 受信画像の保存と状態（WorkProcessor）
- **新規 `WorkKind.ReceiveImage`** を `WorkProcessor.ProcessAsync` で処理。
- 手順:
  1. **idempotency**: `processedEvents.TryMarkNew(WebhookEventId)`。重複配信は二重取得/保存しない。
  2. `lineContent.FetchAsync(messageId, ct)` で本体取得。失敗（取得エラー/上限超過）は `messages.ImageReceiveFailed` / `messages.ImageTooLarge` を返し**状態変更なし**（握りつぶさない）。
  3. `mediaStore.Save(media)` で保存し media id を得る。
  4. **`userState.SetReceivedImage(userId, id)`（新規・原子的）**: `LastImageId=id` / `LastPrompt=null`（🔄再生成が無関係な旧promptで走らないようクリア）/ `AwaitingEdit=true` を**1ロックで一括更新**（worker 並列時の 2 段更新レース回避）。
  5. `messages.ImageReceived`（「画像を受け取りました。どう編集しますか？ 例:…」）を返信（reply トークン→失敗時 push、既存 `SendAsync` 準拠）。
- ※ 生成系の `PrepareMediaAsync` は通さない（プロンプト検証や `PublicBaseUrl` は受信時点では不要。`PublicBaseUrl` は編集**結果**配信時に既存 `HandleImageEditAsync` が確認済み）。
- **レース注意（Option A の非同期特性）**: 受信画像は worker が取得完了後に `SetReceivedImage`（`AwaitingEdit=true`）→プロンプト返信する。その**プロンプト到着前に**ユーザーがテキストを送ると、そのテキストは `AwaitingEdit=false` の状態で**現在モードで**解釈される（編集にはならない）。期待挙動として「編集指示は受信プロンプトが返ってきた後に送る」を前提とする（実装変更は不要、文書化のみ）。

### 2.4 編集の実行（既存フロー再利用・変更なし）
- 続く非コマンドテキストは、既存 `MessageDispatcher` の `AwaitingEdit` 分岐で `WorkKind.ImageEdit`（`RefImageId=LastImageId`）として enqueue され、既存 `WorkProcessor.HandleImageEditAsync` が **`Qwen/Qwen-Image-Edit`** で編集 → 結果画像＋ **QuickReply `[🔄][✏️][💬]`** を push。
- キャンセル: `AwaitingEdit` 中のスラッシュコマンド / モード切替 / 🔄再生成 は既存どおり編集をキャンセル。
- 参照画像が TTL 失効していれば既存 `messages.EditImageExpired`。

### 2.5 i18n（en/ja）
- `UserMessages` に追加（en 既定 / ja）:
  - `ImageReceived`（受信 → 編集プロンプト。例文込み）
  - `ImageReceiveFailed`（取得失敗、再送を促す）
  - `ImageTooLarge`（上限超過。上限MBを含めても可）
  - `ImageSourceUnsupported`（外部提供画像などで取得不可）
- ログ/コメントは英語のまま（[[language-and-docs-conventions]]）。

## 3. 設定（追加）
| キー | 既定 | 説明 |
|---|---|---|
| `Line__MaxIncomingImageBytes` | `10485760`（10MB） | 受信画像の取得上限。超過は拒否 |
| `Line__ContentFetchTimeoutSeconds` | `30` | LINE Content API 取得のタイムアウト |

`.env.example` / README(EN/JA) / CLAUDE.md（アーキ要点に「画像受信→編集」を追記、設定表に2キー）へ反映。
2キーは `LineOptions` に追加し、同クラスの doc-comment を **「認証情報（Secret/Token）＋受信画像の取得制約」** へ更新する（現状は credentials 限定の記述）。

## 4. 受入基準（テスト可能）
1. 画像メッセージ（`contentProvider.type=line`）受信 → blob 取得成功 → `MediaStore` 保存 → `LastImageId` 更新＋`AwaitingEdit=true`＋`LastPrompt` クリア。返信は `ImageReceived`。
2. 続く非コマンドテキスト（例「背景を夜空に」）→ image-to-image（Qwen-Image-Edit）で編集され、結果画像＋`[🔄][✏️][💬]` が push。
3. 画像受信は**現在モードに非依存**（chat/image/video いずれでも編集フローに入る）。
4. サイズ上限超過 → `ImageTooLarge`（既定10MB）。状態変更なし・保存なし。
5. `contentProvider.type=external` → `ImageSourceUnsupported`。外部URLを取得しない。状態変更なし。
6. `AwaitingEdit` 中に新しい画像を受信 → 新画像で `LastImageId` 上書き＋再プロンプト（前の画像は破棄）。
7. 取得失敗（LINE API エラー/タイムアウト）→ `ImageReceiveFailed`。状態変更なし。エラーを握りつぶさない。
8. i18n: `App__Locale=en/ja` で受信・失敗・非対応の各文言が切替。
9. webhook は即200（取得は worker がキュー経由で実行）。
10. idempotency: 同一 `webhookEventId` の再配信で二重取得/二重保存しない。
11. `SetReceivedImage` は `LastImageId`/`LastPrompt`/`AwaitingEdit` を単一ロックで一括更新（原子性）。
12. 既存テスト回帰なし（現状45件緑を維持しつつ新規追加）。

### テスト観点（実装時の目安）
- `ReadCappedAsync`: 上限未満→全バイト返す／上限超過→失敗（例外 or null）／空ストリーム。
- Dispatcher ルーティング: `ImageMessageContent(line)` → `WorkKind.ReceiveImage` enqueue（`Text`=messageId）。`external` → enqueue せず `ImageSourceUnsupported` 返信。
- `UserStateStore.SetReceivedImage`: 3項目一括更新・`LastPrompt` が null 化。
- （可能なら）`WorkProcessor` ReceiveImage: fake `ILineContentService` で保存＋状態更新＋返信を検証。

## 5. 実装フェーズ
単一フェーズ（3b の編集フローは既存・main マージ済みのため、入力経路の追加のみ）。
1. `WorkKind.ReceiveImage`＋`WorkItem` の messageId 運搬。
2. `ILineContentService`/`LineContentService`（`MessagingClient` 注入＋`client.Blob.V2.Bot.Message[messageId].Content.GetAsync` 取得＋`ReadCappedAsync`＋タイムアウト）と DI 登録。
3. `MessageDispatcher` に `ImageMessageContent` 分岐（line/external 判定・enqueue/decline）。
4. `WorkProcessor.HandleReceiveImageAsync`（取得→保存→`SetReceivedImage`→プロンプト）。
5. `UserStateStore.SetReceivedImage`（原子的）。
6. `LineOptions` に上限/タイムアウト、`UserMessages` en/ja、ドキュメント反映。
7. テスト追加。

## 6. 決定事項（2026-08-15 確定）
- [x] スコープは **編集のみ**。vision チャット（VQA）は別の後続 spec（05）に回す。
- [x] 受信時 UX は **Option A（即編集プロンプト＋`AwaitingEdit`）**。ボタン確認（B）・モード限定（C）は不採用。
- [x] 受信画像は **どのモードでも**編集フローに入る（モード非依存）。
- [x] 取得は **DI 登録済み `MessagingClient` を注入**し `client.Blob.V2.Bot.Message[messageId].Content.GetAsync(ct)`（データプレーン）で行う。`MessagingBlobApiClient` の直接注入はしない（DI 未登録）。外部URL自前取得はしない（SSRF回避）。
- [x] `contentProvider.type=external` は v1 非対応（`ImageSourceUnsupported`）。
- [x] サイズ上限 10MB（`Line__MaxIncomingImageBytes`）・取得タイムアウト 30秒（`Line__ContentFetchTimeoutSeconds`）。
- [x] 状態更新は原子的 `SetReceivedImage`（`LastImageId`/`LastPrompt=null`/`AwaitingEdit=true`）。
- [x] 受信本体は `MediaStore`（TTLキャッシュ）に保存し、既存 3b 編集フロー（`Qwen/Qwen-Image-Edit`）へ接続。受信時に `/media/{id}` へは配信しない。
- [x] 受信プロンプトは QR なしの素テキスト（既存 ✏️ の `EditPrompt` と同じ挙動）。

## 7. 参考（流用元 / 連携）
- 編集本体: `Ai/ImageEditService.cs`（3b、`Qwen/Qwen-Image-Edit`、`{inputs=base64, parameters.prompt}`）。
- 状態: `State/UserStateStore.cs`（`SetLastImageId`/`SetAwaitingEdit`/`Reset` を拡張）。
- 実行/配信: `Queue/WorkProcessor.cs`（`HandleImageEditAsync` に接続）・`Media/MediaStore.cs`（TTL保存）・`Line/LineMessenger.cs`。
- SDK: `MessagingClient.Blob`（データプレーン facade。`Line.OpenApi.Messaging.Generated.Blob.V2.Bot.Message.Item.Content.ContentRequestBuilder.GetAsync` → Stream、host `api-data.line.me`、summary「Download image, video, and audio data sent from users」）、webhook `ImageMessageContent`（`.Id` / `.ContentProvider?.Type`＝enum `ContentProvider_type{Line,External}`）。
