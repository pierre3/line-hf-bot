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
- **HF Inference**: text-to-image / text-to-video は `{"inputs","parameters"}` を POST → **生メディアバイト**。認証 `Bearer hf_***`。router `https://router.huggingface.co/hf-inference/models/{modelId}`

## アーキテクチャ要点
- webhook は署名検証後**即 200**。生成は `System.Threading.Channels` + `BackgroundService` で非同期処理し、完了後に **Push API** で送信（reply トークンは短命なため）。
- モード状態: per-user に現在モード（chat/image/video）をメモリ保持し、**素メッセージを現在モードで解釈**（既定 chat）。`/image`・`/video`・`/reset`・`/help` は明示上書き。モード切替は**リッチメニュー**（起動時に冪等 provisioning、alias で `richmenuswitch`）。
- 画像結果に **QuickReply**（`🔄 再生成`／`💬 チャットへ`。`✏️ 編集`は image-to-image=3b で追加）。動画結果は `💬 チャットへ`。ボタンは postback で `MessageDispatcher` が処理。
- 生成メディアは **メモリ内 TTL キャッシュ**（既定10分、`IMemoryCache`）で保持し `/media/{id}` 配信。
- 会話履歴は LINE userId 毎にメモリ保持（件数上限あり）。

## 設定（すべて環境変数 / appsettings）
環境変数は section 区切りを `__` で表す（例: section `App` の `PublicBaseUrl` → `App__PublicBaseUrl`）。
- `Line__ChannelSecret`, `Line__ChannelAccessToken`
- `HuggingFace__ApiKey`, `HuggingFace__ChatModel` / `ImageModel` / `VideoModel`,
  `HuggingFace__ChatEndpoint`(既定 `https://router.huggingface.co`。SK が `/v1/chat/completions` を付与するため `/v1` は含めない),
  `HuggingFace__ImageEndpoint`(text-to-image。`{model}` を ImageModel で置換。既定 `https://router.huggingface.co/hf-inference/models/{model}`。プロバイダ依存),
  `HuggingFace__VideoEndpoint`(text-to-video。`{model}` を VideoModel で置換。プロバイダ依存。バイト or JSON(URL) 両対応),
  `HuggingFace__ChatTimeoutSeconds`(60) / `ImageTimeoutSeconds`(120) / `VideoTimeoutSeconds`(300)
- `App__PublicBaseUrl`(https 必須), `App__MediaTtlMinutes`(10), `App__VideoEnabled`(既定 false。`/video` はプロバイダ統合が必要なため既定オフ),
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
