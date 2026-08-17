# ドキュメントレビュー — image-to-video (spec08) ドキュメント更新 (2026-08-17)
Verdict: PASS

## 整合チェックリスト
- [x] 設定整合: `ImageToVideoModel` / `ImageToVideoEndpoint` の既定値が BotOptions・appsettings.json・.env.example・README.md・README.ja.md・CLAUDE.md の6者で一致
- [x] 設定整合: ドキュメント記載の全キーが `BotOptions.cs` に実在（phantom キーなし・記載漏れなし）
- [x] `App__VideoEnabled` が text-to-video と image-to-video の両方を gate する記述が `WorkProcessor`/`QuickReplyFactory` の実装と一致
- [x] QuickReply「🎬 Make a video / 🎬 動画にする」文言が `UserMessages.LabelAnimate`（en/ja）と一致
- [x] `PendingAction.Animate` / `action=animate` / `WorkKind.ImageToVideo` の配線記述（CLAUDE.md）が実装と一致
- [x] submit body task 別記述（画像→動画=`{image_url,prompt}`）が spec/実装 (`ImageToVideoService`) と一致
- [x] エンドポイント整合: `/webhook` `/media/{id}` `/health`（＋dev `/dev/imagetovideo`）が Program.cs と一致
- [x] 手順の再現性: トンネル→.env→pull/run→health→Webhook 設定→友だち追加 の順序が実行可能
- [x] コマンド正確性: `devtunnel host`、`docker pull/run`、`line` CLI コマンドが構成と整合
- [x] 秘密情報: プレースホルダのみ。実トークンなし
- [x] XML ドキュメント/コメント（BotOptions・QuickReplyFactory・UserStateStore）が実挙動と矛盾しない
- [x] README.md（英語）の specs 一覧に spec 08 を追記し ja と一致（指摘#1 対応済み）

## 指摘
| # | 重大度 | 箇所 | 問題(実態との差異) | 必要な対応 | 状態 |
|---|---|---|---|---|---|
| 1 | Minor | `README.md`（Documentation › Specs 一覧） | 英語版の specs 一覧が「…07 image Q&A (vision/VQA)」で止まり、`08 image-to-video` が欠落。README.ja.md には記載あり・`docs/specs/08-image-to-video.md` も実在で en/ja 不一致 | README.md の specs 一覧末尾に `08 image-to-video` を追記し ja と揃える | 対応済み（本コミットで修正） |

## 判定理由
image-to-video に伴う設定・文言・配線・エンドポイントの記述は、`BotOptions.cs`／`appsettings.json`／`.env.example`／`WorkProcessor.cs`／`QuickReplyFactory.cs`／`MessageDispatcher.cs`／`UserStateStore.cs`／`UserMessages.cs`／`Program.cs` の実コードと過不足なく一致している。既定値（`fal-ai/wan/v2.2-a14b/image-to-video` と fal キューエンドポイント）は6ドキュメント間で完全一致し、`App__VideoEnabled` が t2v/i2v を1フラグで gate する説明も実装どおり。QuickReply 文言も `LabelAnimate` と一致。秘密情報の混入もなくセットアップ手順は再現可能。Blocker 0 件。唯一の Minor（英語 README の specs 一覧欠落）は合否基準の FAIL 事由に非該当で、本コミット内で修正済み。よって PASS。
