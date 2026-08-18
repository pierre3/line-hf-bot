# CLAUDE.md — line-hf-bot

LINE から Hugging Face のモデル（チャット / 画像生成 / 動画生成）を使える AI ボット。
ASP.NET (.NET 10, Minimal API) で実装し、Docker Hub で公開。個人・小規模利用向けで、
ローカル PC で Docker 実行 → Dev トンネル等で公開 → LINE に接続、という手軽さが売り。

実装の詳細プランは `C:\Users\小林寛忠\.claude\plans\foamy-fluttering-haven.md` を参照。

## 技術スタック
- **.NET 10** / C# 14 / ASP.NET Minimal API（単一プロジェクト `LineHfBot`）
- **LINE**: `Line.OpenApi.Bot`（Messaging + Webhook）。**.NET 10 専用**。
  - 受信: `AddLineWebhook` + `WebhookRequestParser.ParseAsync(body, signature)`
  - 送信: `MessagingClient.CreateWithStaticToken(token)` → Reply / Push
  - 制約: 画像・動画メッセージは**公開 HTTPS URL 必須**（生バイト不可）→ 生成物は `/media/{id}` で自前配信
- **Semantic Kernel**: `Microsoft.SemanticKernel.Connectors.HuggingFace`（チャット）。画像/動画/vision(VQA) は HF Inference を `HttpClient` で直接呼ぶ（vision は SK コネクタが未対応のため OpenAI 互換 `/v1/chat/completions` 直叩き）**SK KernelFunction/Plugin としてラップ**
- **HF Inference**: text-to-image は `{"inputs"}` を POST し応答は**生メディアバイト**または **JSON(URL)** の両対応（実行時に Content-Type で判定、JSON なら URL を SSRF ガード付きで自前再取得＝`MediaRefetchAllowedHosts`）。認証 `Bearer hf_***`、router `https://router.huggingface.co/hf-inference/models/{modelId}`。**text-to-video は hf-inference 非対応**→ image-to-image と同じ **fal-ai 非同期キュー**（submit `{prompt}`→poll→`video.url`→SSRF 再取得。詳細は下記アーキ要点／`Ai/FalQueue.cs`）。

## アーキテクチャ要点
- webhook は署名検証後**即 200**。生成は `System.Threading.Channels` + `BackgroundService` で非同期処理し、完了後に **Push API** で送信（reply トークンは短命なため）。
- モード状態: per-user に現在モード（chat/image/video）をメモリ保持し、**素メッセージを現在モードで解釈**（既定 chat）。`/image`・`/video`・`/reset`・`/help` は明示上書き。モード切替は**リッチメニュー**（起動時に冪等 provisioning、alias で `richmenuswitch`）。
- 画像結果に **QuickReply**（`🔄 再生成`／`✏️ 編集`／`💬 質問`（vision有効時）／`🎬 動画にする`（動画有効時）／`💬 チャットへ`）。`✏️ 編集`は次の非コマンドテキストを編集指示として **image-to-image** で処理（`AwaitingEdit`。モード切替/コマンドでキャンセル）。動画結果は `💬 チャットへ`。vision 回答は `VisionAnswer` QuickReply（編集/[動画]/チャットへ）。ボタンは postback で `MessageDispatcher` が処理。
  - **保留アクションは enum `PendingAction`（None/Edit/VisionQuestion/Animate）で排他管理**（旧 `AwaitingEdit` bool を置換）。`✏️ 編集`→`Edit`、`💬 質問`→`VisionQuestion`、`🎬 動画にする`→`Animate`。次の非コマンドテキストが `Edit`=img2img / `VisionQuestion`=vision / `Animate`=image-to-video へ流れ、処理前に None へ戻す（ワンショット）。モード切替/スラッシュ/再生成でキャンセル。
  - image-to-image / text-to-video / image-to-video は **fal-ai プロバイダ経由**（hf-inference は3つとも非対応）。fal は**非同期キュー**（共通ヘルパー `Ai/FalQueue.cs`）: submit→`status_url` を router 書き換え(`queue.fal.run`→`router.huggingface.co/fal-ai/…?_subdomain=queue`)で poll→`response_url` の結果 URL(fal.media) を SSRF ガード取得。結果 URL 抽出だけ task 別（編集=`images[0].url` / 動画・画像→動画=`video.url`）。submit body も task 別（編集=`{prompt,image_url,image_urls}` / 動画=`{prompt}` / 画像→動画=`{image_url,prompt}`＝参照画像を base64 data URI で送る）。HF トークンは router 以外へ送らない（`queue.fal.run` 始まりのみ書き換え受理）。**fal は hf-inference より 1 回あたりのクレジット単価が高く消費が激しい**（両者とも HF Inference クレジット＝無料枠を消費する点は同じ）。
  - **画像→動画（image-to-video）**（`Ai/ImageToVideoService.cs`, spec08）: 作業中画像（直近の生成/編集結果 or 受信写真）を入力に、`🎬 動画にする`タップ→次テキストをモーション指示に i2v。image-edit（参照画像 data URI 送信）と t2v（fal キュー・`video.url`）の合わせ技で新規プロトコル面なし。`App__VideoEnabled` を **t2v と共用で gate**（OFF 時は Animate ボタン非表示＋実行時 `NotYetImplemented`）。生成画像結果とVision有効時の受信写真選択に相乗り。timeout は `VideoTimeoutSeconds` 流用。**submit body に `aspect_ratio` 必須**（A14B は既定 `auto` だと result 取得時 422＝出力サイズ非対応。`Ai/ImageDimensions.cs` で PNG/JPEG ヘッダから入力寸法を読み、対応3比 `16:9`/`9:16`/`1:1` の最近傍を送る。寸法不明→`1:1`）。
- **ユーザーが送った写真は編集・質問の入力にできる**（モード非依存）: 受信→LINE Content API（`MessagingClient.Blob`）で本体取得→`MediaStore` 保存。取得は上限/タイムアウト付き、`contentProvider.type=external` は非対応（SSRF 回避で外部URLは自前取得しない）。
  - `App__VisionEnabled=true`（既定）: 保存後 **QuickReply「✏️編集」/「💬この画像について質問」** を返す（`Pending=None`）。編集タップ→img2img、質問タップ→次テキストを質問として **vision/VQA**。
  - `App__VisionEnabled=false`: 従来どおり即・編集（`Pending=Edit`＋「どう編集しますか？」）。vision UI は出さない。
- **画像→チャット（vision/VQA）**（`Ai/VisionService.cs`, spec07/09）: 送信写真・生成画像への質問。SK の HF コネクタは vision 不確実のため不採用→**OpenAI 互換 `/v1/chat/completions` を HttpClient 直叩き**（画像は base64 data URI の `image_url` content part、Bearer は router のみ）。エラー契約は **`ChatService` 準拠**＝`AnswerAsync` は表示可能文字列を返し、OCE→`Timeout`・空→`EmptyAnswer` をサービス側変換、非2xx のみ送出（`WorkProcessor` 最上位 catch は OCE 除外→`Error`）。fal 非依存でクレジット消費はチャット並み。新規 SSRF 面なし（結果 URL 再取得なし）。
  - **会話型 vision（マルチターン, spec09）**: vision 回答後、**同じ画像への追い質問を文脈込みで継続**できる「vision セッション」。`UserState` に `VisionImageId`＋`List<VisionTurn>` を保持し、`AnswerAsync(image, mediaType, history, question, ct)` が**毎リクエストで会話全体を再送**（ステートレス API のため。**画像は最初の user ターンにのみ**添付し以降は text のみ）。`AppendVisionTurn` は `App__VisionMaxTurns`(既定8) で直近ターンに丸め。dispatcher ルーティング優先度: (1)`Pending` (2)**セッションアクティブ＆非スラッシュ→追い質問** (3)現在モード。セッション終了は `SetMode`/`SetLastImage`/`SetReceivedImage`/`Reset`（store 内）＋ `regen`/`edit` arm/`animate` arm/スラッシュ（dispatcher で `ClearVisionSession`）。`SetLastImageId`(編集チェーン)・`SetPending`(ask 再arm)は**非 Clear**。**失敗ターン（`Timeout`/`EmptyAnswer`）は蓄積せずセッション未開始**（初回失敗＝spec07 相当ワンショットへフォールバック）。初回成功ターンのみ `VisionFollowupHint` を回答末尾に連結、回答は常に `VisionAnswer` QuickReply（編集/[動画]/チャットへ）付き。期限切れ＝`ClearVisionSession`＋`VisionImageExpired`。
  - **Part 1（spec09）**: 生成/編集画像結果の QuickReply にも `App__VisionEnabled=true` 時 **「💬 質問」ボタン**（`action=ask`）。既存 ask ハンドラが `LastImageId` を参照するため新ロジック不要。
- 生成メディアは **メモリ内 TTL キャッシュ**（既定10分、`IMemoryCache`）で保持し `/media/{id}` 配信。
- 会話履歴は LINE userId 毎にメモリ保持（件数上限あり）。

## 設定（すべて環境変数 / appsettings）
環境変数は section 区切りを `__` で表す（例: section `App` の `PublicBaseUrl` → `App__PublicBaseUrl`）。
- `Line__ChannelSecret`, `Line__ChannelAccessToken`,
  `Line__MaxIncomingImageBytes`(既定 `10485760`＝10MB。ユーザー送信画像の取得上限。超過は拒否) / `Line__ContentFetchTimeoutSeconds`(30。受信画像の Content API 取得タイムアウト)
- `HuggingFace__ApiKey`, `HuggingFace__ChatModel` / `ImageModel` / `VideoModel`,
  `HuggingFace__ChatEndpoint`(既定 `https://router.huggingface.co`。SK が `/v1/chat/completions` を付与するため `/v1` は含めない),
  `HuggingFace__ImageEndpoint`(text-to-image。`{model}` を ImageModel で置換。既定 `https://router.huggingface.co/hf-inference/models/{model}`。プロバイダ依存),
  `HuggingFace__VideoModel`(既定 `fal-ai/wan/v2.2-5b/text-to-video`。text-to-video の fal プロバイダモデルID) / `VideoEndpoint`(既定 `https://router.huggingface.co/fal-ai/{model}?_subdomain=queue`。fal 非同期キューの submit 先。`{model}` 置換。hf-inference は text-to-video 非対応。**fal はクレジット消費が激しい**),
  `HuggingFace__ImageToVideoModel`(既定 `fal-ai/wan/v2.2-a14b/image-to-video`。image-to-video の fal プロバイダモデルID。軽い代替 `fal-ai/wan-i2v`。A14B は t2v 既定 5B より単価高) / `ImageToVideoEndpoint`(既定 `https://router.huggingface.co/fal-ai/{model}?_subdomain=queue`。fal 非同期キューの submit 先。`{model}` 置換。hf-inference は image-to-video 非対応。timeout は `VideoTimeoutSeconds` 流用。**fal はクレジット消費が激しい**),
  `HuggingFace__ImageEditModel`(既定 `fal-ai/qwen-image-edit`。image-to-image の fal プロバイダモデルID) / `ImageEditEndpoint`(既定 `https://router.huggingface.co/fal-ai/{model}?_subdomain=queue`。fal 非同期キューの submit 先。`{model}` 置換。**fal はクレジット消費が激しい**),
  `HuggingFace__VisionModel`(既定 `Qwen/Qwen2.5-VL-72B-Instruct:ovhcloud`。送信写真への画像 Q&A に使う vision チャットモデル。**provider を pin（`model:provider`）し HF 設定で有効化が必要**＝auto だと `model_not_supported`／混雑時 503 `capacity_exhausted`。代替 pin 例=`zai-org/GLM-4.5V:novita`・`google/gemma-3-27b-it:deepinfra`) / `VisionEndpoint`(既定 `https://router.huggingface.co/v1/chat/completions`。OpenAI 互換 chat completions の**フル URL**。ChatEndpoint と違い直叩きなので `/v1/chat/completions` を含める),
  `HuggingFace__MediaRefetchAllowedHosts`(既定 `fal.media;replicate.delivery`。JSON-URL 応答の再取得を許可するホスト。画像・動画共通。ラベル境界一致・**空なら全拒否**),
  `HuggingFace__ChatTimeoutSeconds`(60) / `ImageTimeoutSeconds`(120) / `ImageEditTimeoutSeconds`(120) / `VideoTimeoutSeconds`(300) / `VisionTimeoutSeconds`(120)
- `App__PublicBaseUrl`(https 必須), `App__MediaTtlMinutes`(10), `App__VideoEnabled`(既定 false。text-to-video **と** image-to-video は fal-ai 経由＝クレジット消費が激しく遅いため既定オフ・opt-in。true で `/video` と `🎬 動画にする` の両方を有効化＝1フラグで動画系をまとめて制御),
  `App__VisionEnabled`(既定 **true**。送信写真・生成画像への画像 Q&A。true で写真受信時に 編集/質問 の QuickReply 分岐＋画像結果に `💬質問` ボタン、false で従来どおり即・編集＝vision UI なし。**既定 ON なので写真受信 UX が spec04 から変わる**。fal 非依存でクレジット軽い),
  `App__VisionMaxTurns`(既定 `8`。会話型 vision セッションで保持する Q&A ターン数上限。追い質問のたびに画像＋履歴を再送＝クレジット消費がターン数に比例するのを抑える。0/負は 1 に丸め。`Chat__MaxHistory` とは別軸),
  `App__Locale`(ユーザー向け文言＋リッチメニューの言語。既定 `en`、`ja` 可), `App__RichMenuEnabled`(既定 true。起動時のリッチメニュー provisioning)
- `Queue__Capacity`(100), `Queue__Workers`(2)
- `Chat__MaxHistory`(20)

※ トークン類は**絶対にコミットしない**（`.env` は `.gitignore`、`.env.example` のみ管理）。

## よく使うコマンド
```
dotnet restore / dotnet build / dotnet run
dotnet test
docker build -t line-hf-bot . / docker compose up
```
ローカル公開: `devtunnel host -p 8080`（→ URL を `PublicBaseUrl` と LINE Webhook `{url}/webhook` に設定）

## 開発を支援するツール（導入済み/推奨）
- **dotnet-claude-kit** プラグイン: .NET 10 特化。Roslyn MCP でトークン効率よくコード探索。`/scaffold` `/verify` `/tdd` `/security-scan` `/build-fix` などを活用。
- **C# LSP** (`csharp-lsp`): IntelliSense / リファクタ / 診断。
- **セキュリティ**: `42crunch-api-security-testing`, `claude-security`, 組み込み `/security-review`（Webhook 署名・トークン漏洩・SSRF を重点確認）。
- **MCP（接続済み）**: Context7（Semantic Kernel / line-openapi-dotnet ドキュメント）、Microsoft Learn（ASP.NET / Docker / Container Apps）。ライブラリ仕様は推測せずこれらで確認する。
- **カスタムスキル** `line-webhook-test`: 実機 LINE なしで署名付きイベントをローカル `/webhook` に送って検証。

## レビューゲート（4段階）
実装は `.claude/agents/` の**薄いラッパ**サブエージェントが担うゲートを順に通す。実分析は既存プラグインへ委譲。
1. **仕様** `spec-review-gate`（自前） → 2. **実装** `impl-review-gate`（→ `dotnet-claude-kit:code-review`）
→ 3. **セキュリティ** `security-review-gate`（→ `dotnet-claude-kit:security-scan` 他） → 4. **ドキュメント** `doc-review-gate`（自前）
- 起動はオンデマンド（「仕様ゲート回して」等、または各フェーズ完了時）。
- 記録付きソフトゲート。**FAIL は既定でブロック**（差し戻し）。判定は `docs/reviews/` に残す。詳細は `docs/reviews/README.md`。

## 規約
- モダン C#（primary constructor、collection expression、`IHttpClientFactory`、`TimeProvider`）を用いる。
- 外部 I/O（LINE / HF）失敗はユーザーに通知し、握りつぶさない。
- 秘密情報をログ・コミットに出さない。

### 言語ルール
- **コメント・ログは英語**（開発者/運用向け）。**エンドユーザー向けの文言（LINE 返信など）は `App__Locale` 依存**（配布既定 `en`／`ja` 切替可）。文言は `Text/UserMessages.cs` に en/ja を集約。
- **公開ドキュメントは英語を既定**とし、**日本語版も用意**（`README.md`＝英語 / `README.ja.md`＝日本語）。
  `docs/specs`・`docs/reviews` は内部作業ドキュメントとして日本語で運用。
- 日本語は翻訳調・AI 生成臭を避け、平易で自然な文章にする。
