# 38 実装レビュー — 画像→チャット vision/VQA（spec 07）

- 日付: 2026-08-17
- 対象: spec 07（`docs/specs/07-image-vision-vqa.md`、§8 AC1-13）
- ゲート: 実装（impl-review-gate）
- 委譲分析: dotnet-claude-kit:code-review（Roslyn MCP）
- 判定: **PASS**

## 対象変更
- `LineHfBot/Ai/VisionService.cs`（新規）: `IVisionService` + `HuggingFaceVisionService`。HF router `/v1/chat/completions`（OpenAI 互換）を HttpClient 直叩き。画像は base64 data URI の `image_url` content part。エラー契約は ChatService 準拠（OCE→`Timeout`・空→`EmptyAnswer` をサービス側変換、非2xx のみ送出）。
- `LineHfBot/State/UserStateStore.cs`: `AwaitingEdit(bool)`→`PendingAction { None, Edit, VisionQuestion }` enum 化。`Snapshot.Pending`／`SetPending`／`SetReceivedImage(pending 引数)`。
- `LineHfBot/Queue/WorkProcessor.cs`: `IVisionService` 注入、`WorkKind.Vision`→`HandleVisionAsync`（冪等→失効→ack→push）、`HandleReceiveImageAsync` を `VisionEnabled` で分岐。
- `LineHfBot/Messaging/MessageDispatcher.cs`: `action=ask` postback、pending 分岐（Edit→ImageEdit／VisionQuestion→Vision）、キャンセル箇所を enum 化。
- `LineHfBot/Queue/WorkItem.cs`: `WorkKind.Vision`。
- `LineHfBot/Line/QuickReplyFactory.cs`: `ReceivedImageChoices`（[編集][質問]）。
- `LineHfBot/Text/UserMessages.cs`: vision 文言 en/ja、`LabelAsk`、Help 追記。
- `LineHfBot/Configuration/BotOptions.cs`: `VisionModel`／`VisionEndpoint`／`VisionTimeoutSeconds`、`AppOptions.VisionEnabled`（既定 true）。
- `LineHfBot/Program.cs`: `AddHttpClient<IVisionService, HuggingFaceVisionService>` + `AllowAutoRedirect=false`。
- `appsettings.json`: 新規キー反映。
- テスト: `VisionServiceTests`（新規6）、`UserStateStoreTests`／`WorkProcessorReceiveImageTests`／`MessageDispatcherTests`（更新・追加）、`WorkProcessorVisionTests`（レビュー後追加3＝AC10/11）。

## 委譲分析の結果
- detect_antipatterns（LineHfBot）: 17 件検出、うち spec07 変更ファイル由来は **0 件**（VisionService.cs は検出ゼロ＝OCE catch はフィルタ付き）。残りはすべて既存（Program.cs・GenerationWorker・WorkProcessor 最上位/受信 catch・HfHttp・LineMessenger・RichMenu*）で本変更と無関係。
- get_diagnostics（solution, all）: Errors 0 / Warnings 0 / Info 0。
- get_di_registrations: Duplicates 0 / CaptiveRisks 0（`IVisionService` typed client=transient を scoped WorkProcessor が消費、captive なし）。
- 裏取り: build 0-0・`dotnet test` 79 件緑。

## 受入基準（§8）照合
| # | 基準 | 結果 |
|---|---|---|
| 1 | `{model, messages:[user, content:[text, image_url:data URI]]}` を VisionEndpoint へ Bearer 付き POST | PASS（test Posts_model_and_multimodal_content_with_auth、URL 完全一致） |
| 2 | data URI MIME=保存 content-type（不明時 image/png）、本体 base64 | PASS（test Unknown_media_type_defaults_to_png） |
| 3 | `choices[0].message.content` を返す・空/欠落→`EmptyAnswer`（サービス側判定） | PASS（test Empty_content・Missing_choices） |
| 4 | 非2xx は本文つき例外→最上位 catch（OCE 除外）→`Error` | PASS（test Non_success_status_throws） |
| 5 | 内部タイムアウト→OCE 捕捉し `Timeout`／外側キャンセルは伝播＝無通知 | PASS（ガード条件、record 26 罠回避を実装で確認） |
| 6 | VisionEnabled=true: 受信→`Pending=None`+`ImageReceivedChoose`+choices | PASS（test VisionEnabled_stores_image_and_offers_choices） |
| 7 | VisionEnabled=false: 受信→`Pending=Edit`+`ImageReceived`（spec04 不変） | PASS（test VisionDisabled_stores_image_arms_edit_and_prompts） |
| 8 | `action=ask`: LastImageId 有→`VisionQuestion`+`VisionPrompt`／無→`EditNoImage` | PASS（実装 Dispatcher）／直接テスト無し（当初 Minor#1） |
| 9 | `VisionQuestion` で非コマンド→`WorkKind.Vision`・slash/mode/regen でキャンセル | PASS（test Pending_vision_question_routes_to_Vision、Slash_command_cancels） |
| 10 | HandleVisionAsync: 失効→`VisionImageExpired`／成功→`VisionThinking` reply→answer push | PASS（実装＋**追加テスト** WorkProcessorVisionTests.Success/Expired） |
| 11 | `WebhookEventId` 冪等 | PASS（実装＋**追加テスト** WorkProcessorVisionTests.Duplicate_event_is_skipped） |
| 12 | 既定値がドキュメント・appsettings と一致 | PASS（test Vision_defaults_match_docs） |
| 13 | enum 置換の回帰なし＋spec04 既存テストを両 UX へ更新 | PASS（VisionDisabled/Enabled 両テスト、Pending_edit→ImageEdit 回帰） |

## 指摘
| # | 重大度 | 箇所 | 問題 | 対応 |
|---|---|---|---|---|
| 1 | Minor | MessageDispatcherTests | AC8（action=ask postback）の直接テスト無し | 残置（action=edit 対称ケース実証済み・非ブロック。PostbackEvent は外部モデルのため推測構築を避けた） |
| 2 | Minor | （不在→解消） | AC10（HandleVisionAsync 失効/成功）の WorkProcessor テスト無し | **解消**（WorkProcessorVisionTests 追加） |
| 3 | Minor | （不在→解消） | AC11（Vision 冪等）の直接テスト無し | **解消**（WorkProcessorVisionTests 追加） |
| 4 | Minor | MessageDispatcher.cs | 質問 pending だが LastImageId 空で `EditNoImage`（編集寄り文言） | §8 AC8「EditNoImage 相当」に準拠。ask 起点では実発生稀。対応不要 |

- Blocker / Major: なし。

## 判定理由
重点観点を全確認、差し戻すべき Blocker/Major なし。
- エラー契約: OCE→`Timeout`（ガード付き・内部のみ）／空→`EmptyAnswer`／非2xx 送出→最上位 catch（OCE 除外）→`Error`。ImageEdit/Video の OCE 送出は不採用で record 26 の無応答バグを回避（VisionServiceTests＋Non_success で実証）。
- enum 置換: reset 前 `snapshot.Pending` で分岐先決定、先頭 `SetPending(None)`＝二重解決なし。mode/regen/slash が確実にキャンセル（回帰テスト済）。
- WorkProcessor 分岐/冪等: Vision は TryMarkNew→失効→ack→push、受信は VisionEnabled で原子的分岐（AC6/7/10/11/13 テスト実証）。
- DI: captive なし・SSRF 面増なし（送信先 router のみ・Bearer）・AllowAutoRedirect=false。
- 既定値: BotOptions＝appsettings＝テスト一致。

## 次ゲート
- security-review-gate（§7 重点: トークン送信先=router 限定、data URI 化に伴う画像本文/トークンの非ログ化、新規 SSRF 面が無いこと）。
