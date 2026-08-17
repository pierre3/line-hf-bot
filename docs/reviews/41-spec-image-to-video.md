# 仕様レビュー — image-to-video（写真/生成画像を fal-ai で動画化 / spec08） (2026-08-17)
Verdict: PASS
委譲分析: なし（自前）
対象: docs/specs/08-image-to-video.md

## 受入基準チェックリスト
- [x] 受入基準が明確・検証可能（§5 AC 1-12。submit本文（image_url data URI＋prompt）・二重fal-aiを含むフルURL明記・router書換・poll→video.url抽出・SSRF再取得・allowlist拒否・queue.fal.run以外拒否・タイムアウト・既定値一致・配線・gate・QuickReply分岐・既存テスト回帰まで具体的）
- [x] 未解決点ゼロ（§6 全項目 [x]。TBD/「たぶん」なし。スコープ外＝動画パラメータ/ワンタップ即生成/別プロバイダ/Vision無効時の受信写真Animate導線を明示）
- [x] テスト可能性（各ACがユニットで検証可能。ワイヤ形式は huggingface_hub `FalAIImageToVideoTask` 実装で確定済＝推測でない。実 fal E2E は課金回避で operator 委任＋dev `/dev/imagetovideo`。spec06 の PASS 前例と同方針）
- [x] スコープ整合（テキスト/画像/動画・非同期処理・メディア配信・設定外部化が CLAUDE.md/spec05/06/07 と一貫。fal 非同期キュー・PendingAction 機構・data URI 参照画像送信の合わせ技として矛盾なし）
- [x] 非機能（秘密情報＝HFトークンは router のみ（FalQueue.ToRouterUrl が queue.fal.run 以外を拒否）／SSRF＝MediaRefetch allowlist＋Authorizationなし＋AllowAutoRedirect=false／PublicBaseUrl＝PrepareMediaAsync 必須／失敗通知＝HfHttp.EnsureSuccess＋最上位 catch→Error）

## 既存コードとの整合検証（裏取り済み）
- FalQueue（`Ai/FalQueue.cs`）: submit→poll→ToRouterUrl 書換（queue.fal.run→router.huggingface.co/fal-ai/＋`_subdomain=queue`、それ以外拒否）は spec08 が再利用する共通経路と一致。URL 抽出のみ task 固有＝spec06/05 と同責務分割。
- 参照画像 data URI 送信: `HuggingFaceImageEditService` が `image_url`＝base64 data URI を submit body に載せる方式（自前ホスト不要）と同型。spec08 は `image_url` のみ（`image_urls` 無し）＋`prompt`＝huggingface_hub `FalAIImageToVideoTask._prepare_payload_as_dict` と一致。
- video.url 抽出: `HuggingFaceVideoService.ExtractVideoUrl`（`video.url`）と同形の private ヘルパー方針＝t2v と同一・妥当。
- MediaRefetch: fal.media allowlist 済・Authorization なし・https 限定・空なら全拒否。動画 mp4 補完（空なら video/mp4）も VideoService と一致。
- 配線: `WorkKind`（enum。ImageToVideo 追加）／`PendingAction`（None/Edit/VisionQuestion。Animate 追加）／MessageDispatcher の保留解決ブロック（snapshot.Pending を捕捉→SetPending(None)→pendingKind 分岐、LastImageId を RefImageId に載せ画像無しは EditNoImage）は現行実装と整合。postback edit/ask と同型の animate 追加も自然。
- QuickReplyFactory: 現状 UserMessages のみ注入・computed property（ImageResult/ReceivedImageChoices/VideoResult）。§3.2 の IOptions<AppOptions> 注入で property 内から VideoEnabled 参照＝WorkProcessor 呼び出し側（`quickReplies.ImageResult` 等）は無改修で成立。
- WorkProcessor: HandleImageToVideoAsync は PrepareMediaAsync（冪等・prompt空・PublicBaseUrl・ack）→ RefImageId を mediaStore.TryGet（失効＝EditImageExpired）→ GenerateAsync → Save → PushVideoAsync（VideoResult＋VideoPreview.Path）で、HandleVideoAsync/HandleImageEditAsync の組合せとして成立。gate（VideoEnabled=false→NotYetImplemented）も t2v の switch 内 gate と同型。
- DI/dev: `AddHttpClient<...>(...).ConfigurePrimaryHttpMessageHandler(AllowAutoRedirect=false)` は他 fal サービス（Program.cs:54-65）と同型。`/dev/imageedit`（Program.cs:173）を範に `/dev/imagetovideo` を追加する方針も一致。
- 設定: HuggingFaceOptions に ImageToVideoModel/ImageToVideoEndpoint を新設（既定は t2v/edit と同テンプレート）。VideoTimeoutSeconds 流用・MediaRefetchAllowedHosts/ApiKey 共通も既存構造と一貫。

## 指摘
| # | 重大度 | 箇所 | 問題 | 必要な対応 |
|---|---|---|---|---|
| 1 | Minor | §5 AC7 / WorkProcessor 最上位 catch | image-to-video のタイムアウトは linked CTS→OCE 送出だが、`ProcessAsync` の最上位 catch は `when (ex is not OperationCanceledException)` で OCE を除外。t2v/edit と同じ「既存 OCE 経路」＝タイムアウト時は「生成中」ack のみでユーザーへ完了/失敗通知が出ない。spec08 が導入する退行ではなく edit/video の既存前例に忠実だが、CLAUDE.md「I/O 失敗は通知し握りつぶさない」との差は残る。 | 現状維持で可（前例踏襲）。ただし実装ゲートで「i2v タイムアウト UX は t2v/edit と同じく無通知」であることを明示し、将来メディア生成系のタイムアウト通知を横断改修する場合の対象に含める旨をメモ。仕様変更は不要。 |
| 2 | Minor | §4 反映リスト | §3.3 は ImageToVideoModel/ImageToVideoEndpoint を `BotOptions.cs` に追加すると明記する一方、§4「反映先」列挙に `BotOptions.cs` が無い（記録33 spec06 の Minor#1 と同種）。 | §4 反映先に `BotOptions.cs` を追記し、コード既定＝§4 表＝appsettings.json の三者一致を実装ゲートで確認。 |
| 3 | Minor | §3.2 postback / gate | `action=animate` は VideoEnabled に依らず Pending=Animate＋AnimatePrompt を返し、次テキストで初めて NotYetImplemented になる。ボタンは VideoEnabled=true でしか出ないため通常到達しない防御パスだが、無効時に AnimatePrompt→NotYetImplemented の二段で分かりにくい。 | 許容可（防御専用）。より綺麗にするなら animate case 冒頭で VideoEnabled=false のとき NotYetImplemented を即返して early-return。仕様変更は必須でない。 |
| 4 | Minor | §3.4 Help | Help は静的で VideoEnabled に依らず「動画化」を案内する（既定 OFF）。ただし既存 Help も既定 OFF の `/video` を常時案内しており前例と一貫。 | 受容可（既存挙動に合わせる）。無効機能を出したくない場合のみ文言調整。 |
| 5 | Minor | §3.1 / §6 mime | data URI の mime は GeneratedMedia.ContentType（空なら image/png フォールバック）。huggingface_hub 参照は `_as_url(default_mime_type="image/jpeg")`。常に実 ContentType を送るため fal 側は宣言 mime を使い実害なし（png/jpeg いずれも受理）。ImageEditService の image/png 固定より厳密。 | 対応不要（情報として記録）。 |

## 判定理由
Blocker 0・Major 0。受入基準（AC 1-12）は submit 本文・二重 `fal-ai/` を含むフル URL・router 書換によるトークン漏洩防止・poll→`video.url`→SSRF 再取得・allowlist/queue.fal.run 以外拒否・タイムアウト・既定値整合・配線（PendingAction.Animate/WorkKind.ImageToVideo）・gate（VideoEnabled 流用）・QuickReply 分岐・既存テスト回帰まで具体的で検証可能。ワイヤ形式は huggingface_hub `FalAIImageToVideoTask` 実装で確定済み（推測でない）。FalQueue／MediaRefetch／WorkProcessor／MessageDispatcher／UserStateStore／QuickReplyFactory／BotOptions／Program.cs いずれも実コードで裏取りし、spec08 の「新規プロトコル面ほぼ無し・spec05+06 の合わせ技」という主張が成立することを確認。スコープはユーザー要求・CLAUDE.md・spec05/06/07 と一貫し、§6 の決定は全て確定済み。残 Minor 5 件はいずれも実装フェーズ内で吸収可能で差し戻し不要。実装フェーズへ進める品質。
