# デプロイ: Docker Hub（CI/CD）と LINE 動作確認の手順

[English](docker-hub.md) | 日本語

このガイドで扱うのは 2 つです。

1. イメージを Docker Hub に**公開**する — GitHub Actions による自動、または手動。
2. 公開したイメージを**動かして**、LINE アプリで一通り**動作確認**する。

関連: [Azure Container Apps へのデプロイ](azure-container-apps.ja.md)。

---

## 1. Docker Hub へ公開する

### 方法A — 自動（GitHub Actions、推奨）

リポジトリには 2 つのワークフローが入っています。

- `.github/workflows/ci.yml` — `main` への push/PR ごとに build＋test（および push なしの Docker ビルド）を実行。
- `.github/workflows/release.yml` — **バージョンタグ**（`v*`）を push すると、**マルチアーキ**（`linux/amd64`・`linux/arm64`）でイメージをビルドし Docker Hub へ push。

**初回セットアップ**

1. Docker Hub のアクセストークンを作成: Docker Hub →*Account Settings → Personal access tokens → Generate new token*（スコープ **Read & Write**）。
2. GitHub リポジトリに 2 つの Secret を追加（*Settings → Secrets and variables → Actions → New repository secret*）:
   - `DOCKERHUB_USERNAME` — Docker Hub のアカウント／名前空間（イメージの名前空間にもなります）。
   - `DOCKERHUB_TOKEN` — 手順 1 のアクセストークン。

**リリース**

```bash
git tag v1.0.0
git push origin v1.0.0
```

公開されるタグ:

- `<DOCKERHUB_USERNAME>/line-hf-bot:1.0.0`
- `<DOCKERHUB_USERNAME>/line-hf-bot:1.0`（major.minor）
- `<DOCKERHUB_USERNAME>/line-hf-bot:latest`

`v1.0.0-rc1` のようなプレリリースタグは公開されますが、`latest` は**動かしません**。

### 方法B — 手動

```bash
docker build -t <ユーザー名>/line-hf-bot:1.0.0 -t <ユーザー名>/line-hf-bot:latest .
docker login
docker push <ユーザー名>/line-hf-bot:1.0.0
docker push <ユーザー名>/line-hf-bot:latest
```

> 企業の TLS 検査プロキシ配下ですか？ ビルド前にルート CA の `*.crt` を `certs/` に置けば、Dockerfile がそれを信頼します（`*.crt` は gitignore 済みで公開されません）。

---

## 2. イメージを動かして LINE で確認する

公開イメージは Docker が動く場所ならどこでも実行できます — 手元の PC、VM、コンテナホストなど。フルマネージドで動かすなら [Azure Container Apps](azure-container-apps.ja.md) を参照。

### 事前準備

- **LINE Messaging API チャネル** — **チャネルシークレット**と長期の**チャネルアクセストークン**。
- **Inference Providers** 権限つきの **Hugging Face トークン**。
- アプリの**公開 HTTPS URL**。ローカルのポートに向けたトンネル（Dev Tunnels / ngrok / Cloudflare Tunnel）か、HTTPS エンドポイントをくれるクラウドホストのいずれか。LINE は Webhook にも、ボットが返す画像 URL にも HTTPS を要求します。

### 手順1 — `.env` を作る

[`.env.example`](../../.env.example) をひな型に、少なくとも以下を埋めます。

```dotenv
Line__ChannelSecret=...
Line__ChannelAccessToken=...
HuggingFace__ApiKey=hf_...
App__PublicBaseUrl=https://<公開ホスト>    # 末尾スラッシュなし
```

`App__PublicBaseUrl` は、このアプリに**外から**届く HTTPS ベース URL にします。ボットはこれを使って `/media/{id}` の画像 URL を組み立て、LINE がそれを取りに来ます。

### 手順2 — 起動

```bash
docker run --env-file .env -p 8080:8080 <ユーザー名>/line-hf-bot:latest
```

ヘルスチェックで確認:

```bash
curl http://localhost:8080/health      # -> {"status":"ok"}
```

ローカル実行をトンネルで公開する場合は、ここでトンネルを起動し、その HTTPS URL を `App__PublicBaseUrl` に設定します（`.env` を変えたらコンテナを再起動）。

```bash
devtunnel host -p 8080 --allow-anonymous
```

### 手順3 — LINE の Webhook を向ける

[LINE Developers コンソール](https://developers.line.biz/) でチャネルを開き →*Messaging API*、**Webhook の利用**をオン、**応答メッセージ**／**あいさつメッセージ**はオフにします。

Webhook URL を `https://<公開ホスト>/webhook` に設定します。`line` CLI が便利です。

```bash
dotnet tool install -g Line.OpenApi.Tools
line config set default --token "チャネルアクセストークン"
line webhook set-endpoint --url "https://<公開ホスト>/webhook"
line webhook test-endpoint          # 成功 / 200 になればOK
```

### 手順4 — アプリで確認

1. コンソール（*Messaging API*）の QR からボットを友だち追加。
2. 普通のメッセージを送る → **チャット**の返信が返る。
3. `/image a cat on a skateboard` → 生成された**画像**が 🔄 / ✏️ / 💬 ボタン付きで返る。
4. **✏️ 編集**をタップ（または写真を送る）→ 編集指示を送る → **編集後の画像**（有料の fal-ai を使用）。
5. 任意: `App__VideoEnabled=true` なら `/video ...` → **動画**（fal-ai、有料かつ遅い）。

チャットは動くのに画像編集／動画がエラーになる場合は、HF トークンに **Inference Providers 権限**と**クレジット**があるか確認してください。画像編集と動画は有料の **fal-ai** を使います。

### トラブルシュート

| 症状 | 考えられる原因 |
| --- | --- |
| Webhook テストが失敗 | `App__PublicBaseUrl`／Webhook URL が HTTPS でない・到達できない／コンテナ未起動 |
| チャットは返るが画像が出ない | `App__PublicBaseUrl` が誤り or 外部到達不可（LINE が `/media/{id}` を取得できない） |
| 編集／動画で「エラー」 | HF トークンに Inference Providers 権限またはクレジットがない（fal-ai は有料） |
| リッチメニューが出ない | `App__RichMenuEnabled=false`、またはトークンにリッチメニュー権限がない |

> **状態について:** 会話履歴と生成メディアは**メモリ内**に保持され、再起動で消えます。**単一インスタンス**で運用してください — [制限事項](../../README.ja.md#制限事項)を参照。
