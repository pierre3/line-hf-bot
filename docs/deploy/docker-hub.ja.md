# デプロイ: Docker Hub から取得して起動（と LINE 設定）

[English](docker-hub.md) | 日本語

Docker Hub の公開イメージを取得し、`.env` で設定して起動し、LINE につなぐ手順です。これがボットの通常の
動かし方です — ソースコードや .NET SDK は**不要**です。

関連: マネージドで動かすなら [Azure Container Apps](azure-container-apps.ja.md) · [自分でイメージを公開する](#付録--自分でイメージを公開するcicd)（メンテナ向け）。

---

## 事前準備

- **LINE Messaging API チャネル** — **チャネルシークレット**と長期の**チャネルアクセストークン**
  （LINE Developers コンソール → チャネル →*Messaging API* /*Basic settings*）。
- **Inference Providers** 権限つきの **Hugging Face トークン**。すべての生成が Inference Providers の
  **クレジット**（毎月の無料枠あり）を消費します。画像編集と動画で使う fal-ai は hf-inference より
  1 回あたりの単価が高く、クレジットの減りが速い点に注意してください。
- アプリの**公開 HTTPS URL**。ローカルのポートに向けたトンネル（Dev Tunnels / ngrok / Cloudflare Tunnel）か、
  HTTPS エンドポイントをくれるクラウドホスト。LINE は Webhook にも、ボットが返すメディア URL にも HTTPS を要求します。
- **Docker**。

---

## 1. 公開 HTTPS URL を用意（先に確定する）

アプリはインターネットから HTTPS で到達できる必要があります — LINE は Webhook にも、ボットが返す
`/media/{id}` URL にも HTTPS を要求します。**先にこれを実行**してください。この URL が次の手順で
`App__PublicBaseUrl` になります。

ローカル実行なら、トンネルを起動してそのままにしておきます。

```bash
devtunnel host -p 8080 --allow-anonymous
```

表示された `https://…devtunnels.ms` の URL を控えます。（ngrok や Cloudflare Tunnel でも構いません。）
クラウドでホストする場合は、そのプラットフォームの HTTPS エンドポイントを使います
（[Azure Container Apps ガイド](azure-container-apps.ja.md)参照）。

---

## 2. `.env` ファイルを作る

コンテナの設定はすべて**環境変数**で行います。渡し方が一番簡単なのは、`--env-file`（または Docker Compose の
`env_file:`）で読み込む `.env` ファイルです。

設定名は `セクション__キー` の形式で、セクションとキーの間は**アンダースコア2つ**（`__`）です
（例: セクション `App`・キー `PublicBaseUrl` → `App__PublicBaseUrl`）。

`.env` という名前のファイルを作り、最低限この4つを埋めます（`App__PublicBaseUrl` には手順 1 のトンネル URL を貼ります）。

```dotenv
# 必須
Line__ChannelSecret=<チャネルシークレット>
Line__ChannelAccessToken=<チャネルアクセストークン>
HuggingFace__ApiKey=hf_xxxxxxxxxxxxxxxxx
App__PublicBaseUrl=https://<公開ホスト>      # 末尾スラッシュなし
```

`App__PublicBaseUrl` は、このアプリにインターネットから**外部到達**できる HTTPS ベース URL にします。ボットは
これを使って `/media/{id}` の画像・動画 URL を組み立て、LINE がそれを取りに来ます。（トンネル利用時はトンネルの
HTTPS URL、クラウド利用時はアプリの HTTPS エンドポイント。）

これ以外は妥当な既定値があり任意です。変えたいものだけ追記してください。

### パラメータ一覧

**必須**

| 変数 | 説明 |
| --- | --- |
| `Line__ChannelSecret` | LINE チャネルシークレット。Webhook 署名の検証に使用。 |
| `Line__ChannelAccessToken` | LINE 長期チャネルアクセストークン。メッセージ送信・リッチメニュー作成に使用。 |
| `HuggingFace__ApiKey` | **Inference Providers** 権限つき HF トークン（編集・動画にはクレジットも必要）。 |
| `App__PublicBaseUrl` | このアプリに外部到達できる公開 **HTTPS** ベース URL。末尾スラッシュなし。LINE がメディアを取得するのに必須。 |

**モデル・プロバイダ**（既定のままで動作。別モデル/プロバイダにするとき変更）

| 変数 | 既定値 | 説明 |
| --- | --- | --- |
| `HuggingFace__ChatModel` | `Qwen/Qwen2.5-7B-Instruct` | チャットモデル（非 gated）。gated モデルは事前に HF でライセンス承諾が必要。 |
| `HuggingFace__ChatEndpoint` | `https://router.huggingface.co` | チャットのベース URL。Semantic Kernel が `/v1/chat/completions` を付けるので `/v1` は**含めない**。 |
| `HuggingFace__ImageModel` | `stabilityai/stable-diffusion-3-medium-diffusers` | text-to-image モデル。 |
| `HuggingFace__ImageEndpoint` | `https://router.huggingface.co/hf-inference/models/{model}` | text-to-image エンドポイント。`{model}` が `ImageModel` に置換。 |
| `HuggingFace__ImageEditModel` | `fal-ai/qwen-image-edit` | image-to-image（✏️ 編集）モデル。**fal-ai は 1 回あたりのクレジット単価が高い**。 |
| `HuggingFace__ImageEditEndpoint` | `https://router.huggingface.co/fal-ai/{model}?_subdomain=queue` | 編集の fal 非同期キュー submit 先。`{model}` → `ImageEditModel`。 |
| `HuggingFace__VideoModel` | `fal-ai/wan/v2.2-5b/text-to-video` | text-to-video モデル。**fal-ai はクレジット消費が激しい**。 |
| `HuggingFace__VideoEndpoint` | `https://router.huggingface.co/fal-ai/{model}?_subdomain=queue` | 動画の fal 非同期キュー submit 先。`{model}` → `VideoModel`。 |
| `HuggingFace__MediaRefetchAllowedHosts` | `fal.media;replicate.delivery` | プロバイダ URL からメディア再取得を許可するホスト。ラベル境界一致・**空なら全拒否**。 |

> **hf-inference は image-to-image / text-to-video を提供していません。** これらは **fal-ai**（1 回あたりの単価が高い）が既定です。
> `VideoEndpoint`/`ImageEditEndpoint` を hf-inference に向けると
> `400 "Model not supported by provider hf-inference"` になります。

**アプリの挙動**

| 変数 | 既定値 | 説明 |
| --- | --- | --- |
| `App__VideoEnabled` | `false` | `/video` を有効化。fal の text-to-video は**クレジット消費が激しく遅い**ため既定オフ。`true` で許可。 |
| `App__Locale` | `en` | ユーザー向け文言とリッチメニューの言語（`en` / `ja`）。 |
| `App__RichMenuEnabled` | `true` | 起動時にモード切替リッチメニューを作成（冪等）。`false` で無し。 |
| `App__MediaTtlMinutes` | `10` | 生成メディアをメモリに保持し `/media/{id}` で配信する時間。 |
| `Line__MaxIncomingImageBytes` | `10485760` | 編集用にダウンロードするユーザー写真の上限（バイト）。既定 10MB。 |
| `Line__ContentFetchTimeoutSeconds` | `30` | ユーザー写真取得のタイムアウト。 |

**任意のチューニング**

| 変数 | 既定値 | 説明 |
| --- | --- | --- |
| `HOST_PORT` | `8080` | **Docker Compose 専用** — 公開する host 側ポート（コンテナ内は常に 8080）。アプリ自身は読まない。`docker run` では `-p` を使う。 |
| `HuggingFace__ChatTimeoutSeconds` | `60` | チャットのタイムアウト。 |
| `HuggingFace__ImageTimeoutSeconds` | `120` | text-to-image のタイムアウト。 |
| `HuggingFace__ImageEditTimeoutSeconds` | `120` | 画像編集のタイムアウト。 |
| `HuggingFace__VideoTimeoutSeconds` | `300` | 動画のタイムアウト（遅いので余裕を持たせる）。 |
| `Queue__Capacity` | `100` | キューの最大件数。満杯だとユーザーに「混雑中」と通知。 |
| `Queue__Workers` | `2` | キューを処理する並列ワーカー数。 |
| `Chat__MaxHistory` | `20` | ユーザーごとに保持する会話ターン数（メモリ内）。 |

> 全キーを含む編集用テンプレートはリポジトリの [`.env.example`](../../.env.example) にあります。

---

## 3. イメージを取得して起動

イメージは Docker Hub の [`pierre3/line-hf-bot`](https://hub.docker.com/r/pierre3/line-hf-bot) で公開しています。
`:latest` か、`:1.0.0` のような固定バージョンを使います。（fork して自分でビルドしたイメージを使う場合は、
自分の Docker Hub 名前空間に置き換えてください。）

### `docker run` の場合

```bash
docker pull pierre3/line-hf-bot:latest
docker run --env-file .env -p 8080:8080 pierre3/line-hf-bot:latest
```

別の host ポート（例 8081）で公開するには `-p` の**左側**を変えます: `-p 8081:8080`。

### Docker Compose の場合

`.env` の隣に、公開イメージを使う `compose.yaml` を作ります（ビルド不要）。

```yaml
services:
  line-hf-bot:
    image: pierre3/line-hf-bot:latest
    container_name: line-hf-bot
    ports:
      - "${HOST_PORT:-8080}:8080"   # host ポートを変えるなら .env に HOST_PORT を設定
    env_file:
      - .env
    restart: unless-stopped
```

```bash
docker compose pull
docker compose up -d
```

### 起動確認

```bash
curl http://localhost:8080/health      # -> {"status":"ok"}
```

> **状態はメモリ内です。** 会話履歴と生成メディアは稼働中のコンテナ内だけにあり、再起動で消えます。
> **単一インスタンス**で動かしてください — [制限事項](../../README.ja.md#制限事項)参照。

---

## 4. LINE の Webhook を向ける

[LINE Developers コンソール](https://developers.line.biz/)でチャネルを開き →*Messaging API*、**Webhook の利用**
をオン、**応答メッセージ**／**あいさつメッセージ**はオフにします。

Webhook URL を `https://<公開ホスト>/webhook` に設定します。`line` CLI が便利です。

```bash
dotnet tool install -g Line.OpenApi.Tools
line config set default --token "チャネルアクセストークン"
line webhook set-endpoint --url "https://<公開ホスト>/webhook"
line webhook test-endpoint          # 成功 / 200 になればOK
```

---

## 5. アプリで確認

1. コンソール（*Messaging API*）の QR からボットを友だち追加。
2. 普通のメッセージを送る → **チャット**の返信が返る。
3. `/image a cat on a skateboard` → 生成された**画像**が 🔄 / ✏️ / 💬 ボタン付きで返る。
4. **✏️ 編集**をタップ（または写真を送る）→ 編集指示を送る → **編集後の画像**（fal-ai、クレジット消費大）。
5. 任意: `App__VideoEnabled=true` なら `/video 走る猫` → **動画**（fal-ai、クレジット消費が激しく遅い）。

---

## 新しいバージョンへの更新

```bash
# docker run
docker pull pierre3/line-hf-bot:latest
docker rm -f line-hf-bot && docker run --env-file .env -p 8080:8080 pierre3/line-hf-bot:latest

# docker compose
docker compose pull && docker compose up -d
```

`.env` の値を変えた場合はコンテナを再作成すると反映されます（`.env` はビルド時ではなく起動時に読み込み）。
取得済みイメージを使う限り再ビルドは不要です。

## トラブルシュート

| 症状 | 考えられる原因 |
| --- | --- |
| Webhook テストが失敗 | `App__PublicBaseUrl`／Webhook URL が HTTPS でない・到達できない／コンテナ未起動 |
| チャットは返るが画像が出ない | `App__PublicBaseUrl` が誤り or 外部到達不可（LINE が `/media/{id}` を取得できない） |
| 編集／動画で `400 "Model not supported by provider hf-inference"` | `ImageEditEndpoint`/`VideoEndpoint`（またはモデルID）が hf-inference を指している。上記の fal-ai 既定を使う |
| 編集／動画でその他の「エラー」 | HF トークンに Inference Providers 権限がない、または**クレジット切れ**（fal-ai は消費が速い） |
| リッチメニューが出ない | `App__RichMenuEnabled=false`、またはトークンにリッチメニュー権限がない |

実際のエラーはコンテナログで確認できます: `docker logs line-hf-bot`（`Failed to handle item ...` を探す）。

---

## 付録 — 自分でイメージを公開する（CI/CD）

自分でイメージをビルドして公開する場合（フォークなど）にのみ必要です。利用するだけなら読み飛ばして構いません。

### 自動（GitHub Actions）

リポジトリには 2 つのワークフローがあります。

- `.github/workflows/ci.yml` — `main` への push/PR ごとに build＋test（および push なしの Docker ビルド）。
- `.github/workflows/release.yml` — **バージョンタグ**（`v*`）を push すると、**マルチアーキ**
  （`linux/amd64`・`linux/arm64`）でビルドし Docker Hub へ push。

**初回セットアップ**

1. Docker Hub のアクセストークンを作成（Docker Hub →*Account Settings → Personal access tokens*、スコープ **Read & Write**）。
2. GitHub リポジトリに Secret を2つ追加（*Settings → Secrets and variables → Actions*）:
   - `DOCKERHUB_USERNAME` — Docker Hub のアカウント／名前空間（イメージの名前空間にもなる）。
   - `DOCKERHUB_TOKEN` — アクセストークン。

**リリース**

```bash
git tag v1.0.0
git push origin v1.0.0
```

`<DOCKERHUB_USERNAME>/line-hf-bot:1.0.0`・`:1.0`・`:latest` を公開します。プレリリースタグ（`v1.0.0-rc1`）は
公開されますが `latest` は動かしません。

### 手動

```bash
docker build -t <ユーザー名>/line-hf-bot:1.0.0 -t <ユーザー名>/line-hf-bot:latest .
docker login
docker push <ユーザー名>/line-hf-bot:1.0.0
docker push <ユーザー名>/line-hf-bot:latest
```

> 企業の TLS 検査プロキシ配下ですか？ ビルド前にルート CA の `*.crt` を `certs/` に置けば Dockerfile が信頼します
> （`*.crt` は gitignore 済みで公開されません）。
