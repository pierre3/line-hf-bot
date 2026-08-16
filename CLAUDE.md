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
- **Semantic Kernel**: `Microsoft.SemanticKernel.Connectors.HuggingFace`（チャット）。画像/動画は HF Inference Providers を `HttpClient` で呼び、**SK KernelFunction/Plugin としてラップ**
- **HF Inference**: text-to-image は `{"inputs"}` を POST し応答は**生メディアバイト**または **JSON(URL)** の両対応（実行時に Content-Type で判定、JSON なら URL を SSRF ガード付きで自前再取得＝`MediaRefetchAllowedHosts`）。認証 `Bearer hf_***`、router `https://router.huggingface.co/hf-inference/models/{modelId}`。**text-to-video は hf-inference 非対応**→ image-to-image と同じ **fal-ai 非同期キュー**（submit `{prompt}`→poll→`video.url`→SSRF 再取得。詳細は下記アーキ要点／`Ai/FalQueue.cs`）。

## アーキテクチャ要点
- webhook は署名検証後**即 200**。生成は `System.Threading.Channels` + `BackgroundService` で非同期処理し、完了後に **Push API** で送信（reply トークンは短命なため）。
- モード状態: per-user に現在モード（chat/image/video）をメモリ保持し、**素メッセージを現在モードで解釈**（既定 chat）。`/image`・`/video`・`/reset`・`/help` は明示上書き。モード切替は**リッチメニュー**（起動時に冪等 provisioning、alias で `richmenuswitch`）。
- 画像結果に **QuickReply**（`🔄 再生成`／`✏️ 編集`／`💬 チャットへ`）。`✏️ 編集`は次の非コマンドテキストを編集指示として **image-to-image** で処理（`AwaitingEdit`。モード切替/コマンドでキャンセル）。動画結果は `💬 チャットへ`。ボタンは postback で `MessageDispatcher` が処理。
  - image-to-image / text-to-video は **fal-ai プロバイダ経由**（hf-inference は両方とも非対応）。fal は**非同期キュー**（共通ヘルパー `Ai/FalQueue.cs`）: submit→`status_url` を router 書き換え(`queue.fal.run`→`router.huggingface.co/fal-ai/…?_subdomain=queue`)で poll→`response_url` の結果 URL(fal.media) を SSRF ガード取得。結果 URL 抽出だけ task 別（編集=`images[0].url` / 動画=`video.url`）。submit body も task 別（編集=`{prompt,image_url,image_urls}` / 動画=`{prompt}`）。HF トークンは router 以外へ送らない（`queue.fal.run` 始まりのみ書き換え受理）。**fal は有料**。
- **ユーザーが送った写真も編集入力にできる**（モード非依存）: 受信→LINE Content API（`MessagingClient.Blob`）で本体取得→`MediaStore` 保存→`AwaitingEdit` にして「どう編集しますか？」返信→次テキストで img2img 編集。取得は上限/タイムアウト付き、`contentProvider.type=external` は非対応（SSRF 回避で外部URLは自前取得しない）。
- 生成メディアは **メモリ内 TTL キャッシュ**（既定10分、`IMemoryCache`）で保持し `/media/{id}` 配信。
- 会話履歴は LINE userId 毎にメモリ保持（件数上限あり）。

## 設定（すべて環境変数 / appsettings）
環境変数は section 区切りを `__` で表す（例: section `App` の `PublicBaseUrl` → `App__PublicBaseUrl`）。
- `Line__ChannelSecret`, `Line__ChannelAccessToken`,
  `Line__MaxIncomingImageBytes`(既定 `10485760`＝10MB。ユーザー送信画像の取得上限。超過は拒否) / `Line__ContentFetchTimeoutSeconds`(30。受信画像の Content API 取得タイムアウト)
- `HuggingFace__ApiKey`, `HuggingFace__ChatModel` / `ImageModel` / `VideoModel`,
  `HuggingFace__ChatEndpoint`(既定 `https://router.huggingface.co`。SK が `/v1/chat/completions` を付与するため `/v1` は含めない),
  `HuggingFace__ImageEndpoint`(text-to-image。`{model}` を ImageModel で置換。既定 `https://router.huggingface.co/hf-inference/models/{model}`。プロバイダ依存),
  `HuggingFace__VideoModel`(既定 `fal-ai/wan/v2.2-5b/text-to-video`。text-to-video の fal プロバイダモデルID) / `VideoEndpoint`(既定 `https://router.huggingface.co/fal-ai/{model}?_subdomain=queue`。fal 非同期キューの submit 先。`{model}` 置換。hf-inference は text-to-video 非対応。**fal は有料**),
  `HuggingFace__ImageEditModel`(既定 `fal-ai/qwen-image-edit`。image-to-image の fal プロバイダモデルID) / `ImageEditEndpoint`(既定 `https://router.huggingface.co/fal-ai/{model}?_subdomain=queue`。fal 非同期キューの submit 先。`{model}` 置換。**fal は有料**),
  `HuggingFace__MediaRefetchAllowedHosts`(既定 `fal.media;replicate.delivery`。JSON-URL 応答の再取得を許可するホスト。画像・動画共通。ラベル境界一致・**空なら全拒否**),
  `HuggingFace__ChatTimeoutSeconds`(60) / `ImageTimeoutSeconds`(120) / `ImageEditTimeoutSeconds`(120) / `VideoTimeoutSeconds`(300)
- `App__PublicBaseUrl`(https 必須), `App__MediaTtlMinutes`(10), `App__VideoEnabled`(既定 false。text-to-video は fal-ai 経由＝有料かつ遅いため既定オフ・opt-in。true で `/video` 有効化),
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
