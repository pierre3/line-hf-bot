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
- 🎬 **動画生成** — `/video 説明`（text-to-video）を **fal-ai** プロバイダ経由で。fal-ai は Hugging Face クレジットの消費が激しく生成も遅いため**既定オフ**（`App:VideoEnabled`）。`true` で有効化
- 🎛️ **モード切替リッチメニュー** — 下部メニューで チャット / 画像 / 動画 を切替。素のメッセージは
  現在モードで解釈されるのでプレフィックス不要。画像結果には 🔄 再生成 ／ ✏️ 編集（image-to-image）／ 💬 チャットへ ボタン
- 🖼️ **写真を送る** — **✏️ 編集**（image-to-image）か **💬 この画像について質問**（画像 Q&A）を選べる。どちらかを押してから指示や質問を送る（`App:VisionEnabled=false` なら写真は従来どおり即・編集フロー）
- 🔍 **画像について質問**（vision/VQA） — 送った写真への 1 回きりの質問に、vision モデルが回答。fal ではなくチャットと同じ HF Inference クレジットを消費。既定オン。トークンで利用できる vision モデルが必要（`HuggingFace:VisionModel`）
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
- **編集・動画はクレジットの消費が激しい。** すべての生成が Hugging Face Inference Providers のクレジット
  （毎月の無料枠あり）を消費します。画像編集と動画で使う **fal-ai** は hf-inference のチャット/画像より
  1 回あたりの単価が高く、クレジットが一気に減るので注意してください。動画は既定オフです。

## はじめかた

いちばん手軽なのは、**公開済みの Docker Hub イメージ**をローカルで動かし、トンネルで公開する方法です
（ソースの取得やビルドは不要）。ほかの方法は[別の動かし方](#別の動かし方)にまとめています。

### 事前準備
- **LINE Messaging API チャネル** — **チャネルシークレット**と**長期のチャネルアクセストークン**
- **Hugging Face トークン**（**Inference Providers** 権限つき）
- **Docker**
- 公開 HTTPS URL を作れるトンネル。以下の手順では [Dev Tunnels](https://learn.microsoft.com/azure/developer/dev-tunnels/)
  を使います（初回のみ `devtunnel user login`）。ngrok や Cloudflare Tunnel でも構いません。

### 1. トンネルを起動 — 先に公開 URL を確定する
先にこれを実行し、`.env` を書く前に HTTPS URL を確定させます。起動したままにしておきます:
```bash
devtunnel host -p 8080 --allow-anonymous
```
表示された `https://…devtunnels.ms` の URL を控えます。これが `App__PublicBaseUrl` になります。

### 2. `.env` を作成
別のターミナルで、控えたトンネル URL を `App__PublicBaseUrl` に貼り、3 つのトークンを埋めます:
```bash
cat > .env <<'EOF'
Line__ChannelSecret=<チャネルシークレット>
Line__ChannelAccessToken=<チャネルアクセストークン>
HuggingFace__ApiKey=hf_xxxxxxxxxxxxxxxxx
App__PublicBaseUrl=https://<トンネル>.devtunnels.ms
EOF
```
これ以外は既定値で動きます。全一覧は[パラメータ一覧](docs/deploy/docker-hub.ja.md#パラメータ一覧)を参照。

### 3. イメージを取得して起動
```bash
docker pull pierre3/line-hf-bot:latest
docker run --env-file .env -p 8080:8080 pierre3/line-hf-bot:latest
```
起動確認（別ターミナルで）:
```bash
curl http://localhost:8080/health      # -> {"status":"ok"}
```

### 4. Webhook を設定
LINE コンソールで「**Webhook の利用**」をオン（応答メッセージはオフ）にします。Webhook URL を
`https://<トンネル>.devtunnels.ms/webhook` に設定するか、`line` CLI
（[Line.OpenApi.Tools](https://github.com/pierre3/line-openapi-dotnet)。.NET SDK が必要）を使います:
```bash
dotnet tool install -g Line.OpenApi.Tools
line config set default --token "チャネルアクセストークン"
line webhook set-endpoint --url "https://<トンネル>.devtunnels.ms/webhook"
line webhook test-endpoint
```

### 5. 話しかける
LINE コンソールの QR からボットを友だち追加して、メッセージを送ります。詳しい手順・パラメータ一覧・
トラブルシュートは **[Docker Hub から取得して起動](docs/deploy/docker-hub.ja.md)** を参照。

### 別の動かし方
- **[Azure Container Apps](docs/deploy/azure-container-apps.ja.md)** — マネージドな HTTPS エンドポイントにホスティング（トンネル不要）。
- **[ソースから動かす](docs/deploy/from-source.ja.md)** — クローンから Docker Compose または `dotnet run` でビルドして起動（開発・カスタマイズ向け）。

## コマンド
| 入力 | 動作 |
| --- | --- |
| 通常のテキスト | 現在モード（チャット / 画像 / 動画）で解釈 |
| 写真 | ✏️編集 / 💬この画像について質問 を提示。どちらかを押すと次のメッセージが適用される（`App:VisionEnabled=false` なら即・編集） |
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
| `HuggingFace__ImageEditModel` / `HuggingFace__ImageEditEndpoint` | 画像編集(image-to-image)。**fal-ai** プロバイダ経由（既定 `fal-ai/qwen-image-edit`）。hf-inference は image-to-image 非対応。fal-ai は hf-inference より 1 回あたりのクレジット単価が高い |
| `HuggingFace__VideoModel` / `HuggingFace__VideoEndpoint` | 動画生成(text-to-video)。**fal-ai** プロバイダ経由（既定 `fal-ai/wan/v2.2-5b/text-to-video`）。hf-inference は text-to-video 非対応。fal-ai はクレジット消費が激しく遅い |
| `HuggingFace__VisionModel` / `HuggingFace__VisionEndpoint` | 送信写真への画像 Q&A。OpenAI 互換エンドポイント上の vision チャットモデル（既定 `Qwen/Qwen2.5-VL-7B-Instruct`）。fal ではなくチャットと同じ HF クレジットを消費。availability は provider 依存で、トークンが利用できないと「質問」ボタンは汎用エラーになる |
| `HuggingFace__MediaRefetchAllowedHosts` | プロバイダ URL からのメディア再取得を許可するホスト（既定 `fal.media;replicate.delivery`、空なら全拒否） |
| `App__PublicBaseUrl` | トンネルの HTTPS ベース URL（画像に必須） |
| `App__Locale` | ユーザー向け文言とリッチメニューの言語（既定 `en`、`ja` 可） |
| `App__RichMenuEnabled` | 起動時にモード切替リッチメニューを作成（既定 `true`） |
| `App__VideoEnabled` | `/video` を有効化（fal-ai の text-to-video はクレジット消費が激しく遅い。既定 `false`） |
| `App__VisionEnabled` | 送信写真への画像 Q&A（既定 `true`）。オン: 写真受信時に 編集/質問 を提示。オフ: 写真は即・編集フロー（vision UI なし） |

## 使っている技術
- .NET 10 / ASP.NET Minimal API
- [pierre3/line-openapi-dotnet](https://github.com/pierre3/line-openapi-dotnet)（`Line.OpenApi.Bot`）
- [Semantic Kernel](https://github.com/microsoft/semantic-kernel)（Hugging Face コネクタ）
- Hugging Face Inference Providers（画像・動画・vision）

## ドキュメント
- デプロイ手順: [`docs/deploy/`](docs/deploy/) — [Docker Hub から取得して起動](docs/deploy/docker-hub.ja.md)、[Azure Container Apps](docs/deploy/azure-container-apps.ja.md)、[ソースから動かす](docs/deploy/from-source.ja.md)
- 仕様: [`docs/specs/`](docs/specs/) — 01 基本、02 画像プロバイダ、03 モード / リッチメニュー / i18n、04 ユーザー写真の編集、05 画像編集(fal-ai)、06 動画(fal-ai)、07 画像 Q&A(vision/VQA)
- レビュー記録（仕様 / 実装 / セキュリティ / ドキュメントの各ゲート）: [`docs/reviews/`](docs/reviews/)
- 開発ガイド: [`CLAUDE.md`](CLAUDE.md)

## ライセンス
[MIT](LICENSE)。
