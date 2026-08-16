# line-hf-bot

[English](README.md) | 日本語

Hugging Face のモデルを使って、LINE で **AI チャット・画像生成・画像編集・動画生成**ができるボットです。
ASP.NET（.NET 10）で作っています。

## 目的
Hugging Face のモデルを、**LINE のチャット UI** から気軽に試せるようにするためのボットです（専用アプリや
Web コンソールは不要）。手元の PC で Docker イメージを動かし、トンネルで公開して LINE につなぐだけ、という
手軽さを目指しています（クラウドに置いても動きます）。**検証用・個人利用**を想定しており、多人数向けのサービスではありません。

## できること
- 💬 **チャット**（会話の流れを覚える。Semantic Kernel + Hugging Face）
- 🎨 **画像生成** — `/image 説明`、または画像モードに切り替えて説明を送るだけ
- 🎬 **動画生成** — `/video 説明`（text-to-video）を **fal-ai** プロバイダ経由で。fal は**有料**かつ生成が遅いため**既定オフ**（`App:VideoEnabled`）。`true` で有効化
- 🎛️ **モード切替リッチメニュー** — 下部メニューで チャット / 画像 / 動画 を切替。素のメッセージは
  現在モードで解釈されるのでプレフィックス不要。画像結果には 🔄 再生成 ／ ✏️ 編集（image-to-image）／ 💬 チャットへ ボタン
- 🖼️ **自分の写真を編集** — 写真を送ると「どう編集しますか？」と聞かれ、次のメッセージで image-to-image 編集
- 🌐 **英語デフォルト・日本語対応**（`App:Locale` = `en`/`ja`）。ユーザー向け文言とリッチメニューが追従
- 🐳 Docker イメージとして配布。ローカル＋トンネルで手軽に、クラウド運用も可能

スラッシュコマンド（`/image`・`/video`・`/reset`・`/help`）はモードに関係なく常に使えます。リッチメニューは
起動時に自動作成されます（`App:RichMenuEnabled=false` で無効化）。

## しくみ
```
LINE → POST /webhook（署名検証して即 200 応答）
     → メモリ内キュー → バックグラウンド処理 → Hugging Face
     → LINE へ返信/プッシュ（画像は /media/{id} で配信）
```
LINE は画像に公開 HTTPS URL を要求するため、生成画像はアプリ自身がホストし、その URL を LINE に渡します。

## 制限事項
手軽・小規模での利用に振った作りです。次のトレードオフに注意してください。

- **すべてインメモリ。** 会話履歴と生成メディア（`/media/{id}` で配信）はプロセスのメモリ上だけにあり
  （メディアは TTL キャッシュ）、**再起動・再デプロイで消えます**。データベースはありません。
- **単一インスタンス限定。** 状態を共有しないため、レプリカを 2 つ以上動かすと履歴が分かれ、メディア URL も
  壊れます。**冗長化やスケールアウトには向きません** — 必ず 1 インスタンスで動かしてください。
- **編集・動画は有料プロバイダ。** 画像編集と動画は **fal-ai** を使い、有料で Hugging Face の
  Inference Providers クレジットが必要です。動画は既定オフです。

## はじめかた

### 事前準備
- **LINE Messaging API チャネル** — **チャネルシークレット**と**長期のチャネルアクセストークン**を取得
- **Hugging Face トークン**（**Inference Providers** 権限つき）
- 公開 HTTPS URL を作れるトンネル（[Dev Tunnels](https://learn.microsoft.com/azure/developer/dev-tunnels/)、ngrok、Cloudflare Tunnel など）
- Docker（ローカル開発なら .NET 10 SDK）

### 1. 設定
```bash
cp .env.example .env
# .env を編集: Line__ChannelSecret, Line__ChannelAccessToken, HuggingFace__ApiKey, App__PublicBaseUrl
```
`App__PublicBaseUrl` はトンネルの HTTPS ベース URL です（LINE が取りに来る画像 URL を組み立てるのに使います）。

### 2. 起動
```bash
docker compose up --build      # :8080 で待ち受け
```
> 8080 が使用中なら `.env` に `HOST_PORT`（例 `HOST_PORT=8081`）を設定し、下のトンネルも同じポートにします。

ローカル開発なら: `dotnet run --project LineHfBot --urls http://localhost:8080`（値は `dotnet user-secrets` で設定。`--urls` で下のトンネル手順と同じ 8080 に揃えます）。

### 3. トンネルで公開
```bash
devtunnel host -p 8080 --allow-anonymous     # 表示された https URL を App__PublicBaseUrl にも設定
```

### 4. Webhook を設定
LINE コンソールで「Webhook の利用」をオン（応答メッセージはオフ）にし、Webhook をトンネルに向けます。
`line` CLI（[Line.OpenApi.Tools](https://github.com/pierre3/line-openapi-dotnet)。グローバルツールとして導入）を使うと簡単です:
```bash
dotnet tool install -g Line.OpenApi.Tools
line config set default --token "チャネルアクセストークン"
line webhook set-endpoint --url "https://<トンネル>/webhook"
line webhook test-endpoint
```

### 5. 話しかける
LINE コンソールの QR からボットを友だち追加して、メッセージを送ります。

## コマンド
| 入力 | 動作 |
| --- | --- |
| 通常のテキスト | 現在モード（チャット / 画像 / 動画）で解釈 |
| 写真 | 「どう編集しますか？」と聞かれ、次のメッセージで編集（image-to-image） |
| `/image 説明` | 画像を生成 |
| `/video 説明` | 動画を生成（text-to-video、fal-ai 経由）。既定オフ、`App:VideoEnabled` 参照 |
| `/reset` | 会話履歴を消し、モードを既定に戻す |
| `/help` | 使い方を表示 |

スラッシュコマンドはモードを変えずにどのモードでも使えます。画像結果には 🔄 再生成 ／ ✏️ 編集 ／ 💬 チャットへ ボタンが付きます。

## 設定
設定はすべて環境変数（`セクション__キー`）です。全一覧は [`.env.example`](.env.example) を参照。主なもの:

| 変数 | 補足 |
| --- | --- |
| `Line__ChannelSecret` / `Line__ChannelAccessToken` | LINE チャネルの資格情報（必須） |
| `Line__MaxIncomingImageBytes` / `Line__ContentFetchTimeoutSeconds` | 編集用に受信するユーザー画像の取得上限/タイムアウト（既定 10MB / 30秒） |
| `HuggingFace__ApiKey` | Inference Providers 権限つき HF トークン（必須） |
| `HuggingFace__ChatModel` | 既定 `Qwen/Qwen2.5-7B-Instruct`（非 gated） |
| `HuggingFace__ImageEditModel` / `HuggingFace__ImageEditEndpoint` | 画像編集(image-to-image)。**fal-ai** プロバイダ経由（既定 `fal-ai/qwen-image-edit`）。hf-inference は image-to-image 非対応。fal は**有料**（Inference Providers のクレジットが必要） |
| `HuggingFace__VideoModel` / `HuggingFace__VideoEndpoint` | 動画生成(text-to-video)。**fal-ai** プロバイダ経由（既定 `fal-ai/wan/v2.2-5b/text-to-video`）。hf-inference は text-to-video 非対応。fal は**有料**かつ遅い |
| `HuggingFace__MediaRefetchAllowedHosts` | プロバイダ URL からのメディア再取得を許可するホスト（既定 `fal.media;replicate.delivery`、空なら全拒否） |
| `App__PublicBaseUrl` | トンネルの HTTPS ベース URL（画像に必須） |
| `App__Locale` | ユーザー向け文言とリッチメニューの言語（既定 `en`、`ja` 可） |
| `App__RichMenuEnabled` | 起動時にモード切替リッチメニューを作成（既定 `true`） |
| `App__VideoEnabled` | `/video` を有効化（fal-ai の text-to-video は有料。既定 `false`） |

## デプロイ
公開とホスティングの手順を 2 つのガイドにまとめています（それぞれ日本語版あり）。

- **[Docker Hub（CI/CD）と LINE 動作確認](docs/deploy/docker-hub.ja.md)** — GitHub Actions（`v*` タグを push）
  または手動でイメージを公開し、イメージを動かして LINE で一通り確認するまで。
- **[Azure Container Apps](docs/deploy/azure-container-apps.ja.md)** — Azure CLI でマネージドな HTTPS
  エンドポイントにホスティング。

CI/CD は配線済みです。`.github/workflows/ci.yml` が push/PR ごとに build＋test、`.github/workflows/release.yml`
がバージョンタグ push でマルチアーキイメージを Docker Hub へ公開します。手動公開の簡易版:

```bash
docker build -t <ユーザー名>/line-hf-bot .
docker push <ユーザー名>/line-hf-bot
# どこでも実行:
docker run --env-file .env -p 8080:8080 <ユーザー名>/line-hf-bot
```

## 使っている技術
- .NET 10 / ASP.NET Minimal API
- [pierre3/line-openapi-dotnet](https://github.com/pierre3/line-openapi-dotnet)（`Line.OpenApi.Bot`）
- [Semantic Kernel](https://github.com/microsoft/semantic-kernel)（Hugging Face コネクタ）
- Hugging Face Inference Providers（画像・動画）

## ドキュメント
- デプロイ手順: [`docs/deploy/`](docs/deploy/) — [Docker Hub と LINE 動作確認](docs/deploy/docker-hub.ja.md)、[Azure Container Apps](docs/deploy/azure-container-apps.ja.md)
- 仕様: [`docs/specs/`](docs/specs/) — 01 基本、02 画像プロバイダ、03 モード / リッチメニュー / i18n、04 ユーザー写真の編集、05 画像編集(fal-ai)、06 動画(fal-ai)
- レビュー記録（仕様 / 実装 / セキュリティ / ドキュメントの各ゲート）: [`docs/reviews/`](docs/reviews/)
- 開発ガイド: [`CLAUDE.md`](CLAUDE.md)

## ライセンス
[MIT](LICENSE)。
