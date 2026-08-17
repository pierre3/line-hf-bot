# 42 実装レビュー — image-to-video（作業中画像を fal-ai で動画化, spec 08）

- 日付: 2026-08-17
- 対象: spec 08（`docs/specs/08-image-to-video.md`、§5 AC1-12）
- ゲート: 実装（impl-review-gate）
- 委譲分析: dotnet-claude-kit:code-review（Roslyn MCP）
- 判定: **PASS**

## 対象変更
- `LineHfBot/Ai/ImageToVideoService.cs`（新規）: `IImageToVideoService` + `HuggingFaceImageToVideoService`。`FalQueue`（submit→poll→result→SSRF 再取得）を再利用し、固有処理は submit body `{image_url=data URI, prompt}` の組み立てと結果 `video.url` 抽出のみ。timeout は `VideoTimeoutSeconds` 流用（linked CTS）。`VideoService` と同形。
- `LineHfBot/Queue/WorkItem.cs`: `WorkKind.ImageToVideo` 追加。`Text`=モーション指示、`RefImageId`=参照画像 media id（doc コメント更新済）。
- `LineHfBot/State/UserStateStore.cs`: `PendingAction.Animate` 追加（enum・doc 更新）。
- `LineHfBot/Messaging/MessageDispatcher.cs`: `IOptions<AppOptions>` 注入。保留解決を `snapshot.Pending` の switch 化（VisionQuestion→Vision / Animate→ImageToVideo / 既定→ImageEdit）。postback `action=animate`（VideoEnabled=false は early-return で `NotYetImplemented`、true は `Pending=Animate`+`AnimatePrompt`）。
- `LineHfBot/Line/QuickReplyFactory.cs`: `IOptions<AppOptions>` 注入。`VideoEnabled` 時のみ `ImageResult`／`ReceivedImageChoices` に Animate ボタン追加。`VideoResult` 不変。
- `LineHfBot/Queue/WorkProcessor.cs`: `IImageToVideoService` 注入。`case WorkKind.ImageToVideo`（VideoEnabled gate→false は `NotYetImplemented`）、`HandleImageToVideoAsync`（`PrepareMediaAsync`→`RefImageId` 失効チェック→生成→`PushVideoAsync`）。
- `LineHfBot/Configuration/BotOptions.cs`: `ImageToVideoModel`（既定 `fal-ai/wan/v2.2-a14b/image-to-video`）／`ImageToVideoEndpoint`（fal 非同期キューテンプレート）追加、`AppOptions.VideoEnabled` doc を i2v 込みに更新。
- `LineHfBot/Text/UserMessages.cs`: `LabelAnimate`／`AnimatePrompt`（en/ja）+ Help 更新。
- `LineHfBot/Program.cs`: `AddHttpClient<IImageToVideoService, HuggingFaceImageToVideoService>` + `AllowAutoRedirect=false`、Development のみ `/dev/imagetovideo`。
- `appsettings.json`／`.env.example`／`README.md`／`README.ja.md`／`CLAUDE.md`: 新規キー・有料注記反映。
- テスト: `ImageToVideoServiceTests`（新規8）、`QuickReplyFactoryTests`（新規4）、`MessageDispatcherTests`（AC9/10・キャンセル追加）、`WorkProcessorVisionTests`／`WorkProcessorReceiveImageTests`（新 DI 依存の stub 追加で更新）。

## 委譲分析の結果
- detect_antipatterns（LineHfBot）: 18 件検出、うち spec08 変更ファイル由来は **0 件**。`ImageToVideoService.cs` は検出ゼロ。残りはすべて既存（Program.cs 空 catch・GenerationWorker・WorkProcessor 最上位 catch(OCE 除外)/best-effort 通知・HfHttp・LineMessenger・RichMenu*）で本変更と無関係（AP005/AP007＝確立済みのエラー処理方針）。
- get_diagnostics（project LineHfBot, all）: Errors 0 / Warnings 0 / Info 0。
- 裏取り: `dotnet build` 0-0・`dotnet test` 94 件緑（申告どおり）。

## 受入基準（§5）照合
| # | 基準 | 結果 |
|---|---|---|
| 1 | submit=`{image_url:data URI, prompt}` を i2v エンドポイントへ Bearer POST | PASS（test Submit_posts_image_url_and_prompt_with_auth、URL 完全一致・data URI 検証） |
| 2 | status_url/response_url を router 書き換え、HF トークンは router のみ | PASS（`FalQueue.ToRouterUrl` 再利用・test Polls_via_router…／host=router.huggingface.co+auth） |
| 3 | COMPLETED まで poll→`video.url` 取得 | PASS（`FalQueue.PollUntilCompletedAsync`＋`ExtractVideoUrl`、test 実証） |
| 4 | `video.url`(fal.media) を allowlist 経由・Authorization なしで再取得、`video/mp4` | PASS（`MediaRefetch` 再利用・test refetch host=v3b.fal.media/HasAuthorization=false） |
| 5 | allowlist 外ホスト結果 URL は拒否 | PASS（test Result_url_on_disallowed_host_is_rejected） |
| 6 | status_url/response_url が queue.fal.run 以外は拒否 | PASS（`FalQueue.ToRouterUrl` 共有ロジック不変＝MediaRefetchGuardTests/既存 fal テストで回帰なし） |
| 7 | timeout は `VideoTimeoutSeconds` で打ち切り（OCE 経路） | PASS（linked CTS、`VideoService` と同一・値=Options で確認） |
| 8 | 既定値がドキュメント・appsettings と一致 | PASS（test ImageToVideo_defaults_match_docs） |
| 9 | animate postback→`Pending=Animate`→次テキストが `ImageToVideo`(RefImageId=LastImageId)。mode/slash/regen でキャンセル | PASS（test Pending_animate_routes_to_ImageToVideo／Animate_postback_arms…／Slash_command_cancels_pending_animate） |
| 10 | VideoEnabled=false: Animate ボタン非表示・`WorkKind.ImageToVideo`→`NotYetImplemented` | PASS（QuickReply omit test／Dispatcher decline test／WorkProcessor gate は実装＝Video 同型・下記 Minor#1） |
| 11 | VideoEnabled=true で ImageResult(Regen/Edit/Animate/Chat)・ReceivedImageChoices(Edit/Ask/Animate)、false で従来 | PASS（QuickReplyFactoryTests 4件で両分岐実証） |
| 12 | 既存テスト（image-edit/fal 動画/dispatcher/quickreply）緑 | PASS（94 件緑、新 DI 依存は stub 追加のみで回帰なし） |

## 指摘
| # | 重大度 | 箇所 | 問題 | 対応 |
|---|---|---|---|---|
| 1 | Minor | WorkProcessor（テスト不在） | `HandleImageToVideoAsync` の gate(VideoEnabled=false→NotYetImplemented)・失効(EditImageExpired)・成功(PushVideo) を直接検証する WorkProcessor テストが無い | 残置（非ブロック）。gate は Dispatcher/QuickReply 層で実証、実装は既存 `HandleVideoAsync`（gate）＋`HandleImageEditAsync`（失効）の逐語ミラーで、両者とも WorkProcessor 単体テストは元来不在＝既存慣行と同水準。将来 WorkProcessorVideoTests 新設時に相乗り推奨 |
| 2 | Minor | ImageToVideoService.cs / VideoService.cs | `ExtractVideoUrl` が両サービスで重複 | 残置。spec06「抽出はサービス固有・重複許容」方針を踏襲（意図的） |
| 3 | Minor | 既存 AP005/AP007 | 空 catch・catch(Exception) | 本変更由来ゼロ。確立済みエラー処理方針（最上位 catch は OCE 除外→`messages.Error` 通知＝握りつぶしなし） |

- Blocker / Major: なし。

## 判定理由
重点観点を全確認、差し戻すべき Blocker/Major なし。
- 既存 fal 実装との整合: `ImageToVideoService` は `VideoService` と構造一致（submit→poll→result→refetch）で差分は `image_url`（data URI）付与のみ＝`ImageEditService` の参照画像手法と同一。新規プロトコル面なし。
- SSRF/トークン漏洩の非退行: 送信は `FalQueue`（router 書き換え・`queue.fal.run` 始まりのみ受理・Bearer は router のみ）、最終取得は `MediaRefetch`（https 限定・allowlist ラベル境界一致・fail-closed・Authorization なし）を**そのまま再利用**。allowlist へのホスト追加なし。test で「status=router+auth／refetch=fal.media 無 auth／allowlist 外拒否」を実証。
- null/失効ハンドリング: `RefImageId` 空 or `TryGet` 失敗 or null→`EditImageExpired`（`HandleImageEditAsync` と同一ガード）。prompt 空は `PrepareMediaAsync`→`AnimatePrompt`。mime 空→`image/png`、refetch type 空→`video/mp4` フォールバック。
- gate の正しさ: 三重防御（QuickReply=ボタン非表示／Dispatcher=disabled postback early-return／WorkProcessor=`WorkKind.ImageToVideo` の VideoEnabled チェック）。arm 後に無効化されても WorkProcessor が `NotYetImplemented`。
- enum 分岐: 保留解決は `SetPending(None)` 先行後に**捕捉済み `snapshot.Pending`**（readonly record struct コピー）で分岐先決定＝二重解決なし。mode/slash/regen でキャンセル（回帰テスト済）。
- DI: typed client(transient) を scoped WorkProcessor が消費＝captive なし。`AllowAutoRedirect=false`（リダイレクト経由の allowlist 迂回防止）を他 fal サービスと同様に付与。
- 既定値一致: BotOptions＝appsettings＝テスト（test ImageToVideo_defaults_match_docs）。

## 次ゲート
- security-review-gate（§重点: トークン送信先=router 限定の非退行、data URI 化に伴う参照画像本文/トークンの非ログ化、新規 SSRF 面が無いこと＝`FalQueue`/`MediaRefetch` 共有経路の確認）。
