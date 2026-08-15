# line-hf-bot

[English](README.md) | 日本語

Hugging Face のモデルを使って、LINE で **AI チャットと画像生成**ができるボットです（動画は予定）。
ASP.NET（.NET 10）で作っています。手元の PC で Docker イメージを動かし、トンネルで公開して
LINE につなぐだけ、という手軽さを目指しています。もちろんクラウドに置いても動きます。

## できること
- 💬 **チャット**（会話の流れを覚える。Semantic Kernel + Hugging Face）
- 🎨 **画像生成** — `/image 説明`、または画像モードに切り替えて説明を送るだけ
- 🎬 動画生成 — 実装の骨組みはあるが**既定オフ**（`App:VideoEnabled`）。動画プロバイダ統合が必要
- 🎛️ **モード切替リッチメニュー** — 下部メニューで チャット / 画像 / 動画 を切替。素のメッセージは
  現在モードで解釈されるのでプレフィックス不要。画像結果には 🔄 再生成 ／ ✏️ 編集（image-to-image）／ 💬 チャットへ ボタン
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
| 通常のテキスト | AI がチャットで返信 |
| `/image 説明` | 画像を生成 |
| `/video 説明` | 既定では無効（`App:VideoEnabled` 参照） |
| `/reset` | 会話履歴を消す |
| `/help` | 使い方を表示 |

## 設定
設定はすべて環境変数（`セクション__キー`）です。全一覧は [`.env.example`](.env.example) を参照。主なもの:

| 変数 | 補足 |
| --- | --- |
| `Line__ChannelSecret` / `Line__ChannelAccessToken` | LINE チャネルの資格情報（必須） |
| `HuggingFace__ApiKey` | Inference Providers 権限つき HF トークン（必須） |
| `HuggingFace__ChatModel` | 既定 `Qwen/Qwen2.5-7B-Instruct`（非 gated） |
| `HuggingFace__ImageEditModel` | ✏️編集ボタンの image-to-image モデル（既定 `Qwen/Qwen-Image-Edit`） |
| `HuggingFace__MediaRefetchAllowedHosts` | プロバイダ URL からのメディア再取得を許可するホスト（既定 `fal.media;replicate.delivery`、空なら全拒否） |
| `App__PublicBaseUrl` | トンネルの HTTPS ベース URL（画像に必須） |

## Docker Hub へ公開
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
- 仕様: [`docs/specs/01-line-hf-bot.md`](docs/specs/01-line-hf-bot.md)
- レビュー記録: [`docs/reviews/`](docs/reviews/)
- 開発ガイド: [`CLAUDE.md`](CLAUDE.md)

## ライセンス
まだ決めていません。
