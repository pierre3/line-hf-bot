# デプロイ: ソースから動かす（クローンしてビルド）

[English](from-source.md) | 日本語

リポジトリを**ローカルにクローン**して、Docker Compose または .NET SDK でビルドして動かす方法です。
**開発・カスタマイズ向け**です。単に動かしたいだけなら、公開イメージを使う
[Docker Hub から取得して起動](docker-hub.ja.md)の方が簡単です。

関連: [Docker Hub から取得して起動](docker-hub.ja.md)・[Azure Container Apps](azure-container-apps.ja.md)。

---

## 事前準備

- リポジトリを**クローン**済みであること。
- **Docker**（Compose を使う場合）または **.NET 10 SDK**（`dotnet run` を使う場合）。
- Docker Hub ガイドと同じ資格情報と公開 URL: **LINE Messaging API チャネル**（チャネルシークレット＋
  アクセストークン）、**Inference Providers** 権限つき **Hugging Face トークン**、**公開 HTTPS トンネル**
  （Dev Tunnels / ngrok / Cloudflare Tunnel）。

---

## 1. トンネルを起動 — 先に公開 URL を確定する

先にこれを実行し、`.env` を書く前に HTTPS URL を確定させます。起動したままにしておきます:

```bash
devtunnel host -p 8080 --allow-anonymous
```

表示された `https://…devtunnels.ms` の URL を控えます。これが `App__PublicBaseUrl` になります。

> 8080 が使用中なら別のポート（例 `-p 8081`）を選び、`.env` に `HOST_PORT=8081`（Compose の場合）を設定し、
> 以降も同じポートに揃えます。

---

## 2. 設定

リポジトリには**全設定**を説明した [`.env.example`](../../.env.example) が同梱されています。コピーして値を埋めます:

```bash
cp .env.example .env
# .env を編集: Line__ChannelSecret, Line__ChannelAccessToken, HuggingFace__ApiKey, App__PublicBaseUrl
```

`App__PublicBaseUrl` には手順 1 のトンネル URL を設定します。各キーの意味は Docker Hub ガイドの
[パラメータ一覧](docker-hub.ja.md#パラメータ一覧)を参照。

---

## 3a. Docker Compose で起動（ローカルでイメージをビルド）

リポジトリの [`compose.yaml`](../../compose.yaml) が `Dockerfile` からイメージをビルドし、`.env` を読み込みます:

```bash
docker compose up --build      # :8080 で待ち受け
```

別のホストポートで公開したい場合は `.env` に `HOST_PORT`（例 `HOST_PORT=8081`）を設定します。コンテナ内部は
常に 8080 で待ち受けます。

## 3b. .NET SDK で起動

Docker を使わず SDK だけで回す場合は、`.env` の代わりに `dotnet user-secrets` にトークンを設定します:

```bash
cd LineHfBot
dotnet user-secrets init
dotnet user-secrets set "Line__ChannelSecret" "<チャネルシークレット>"
dotnet user-secrets set "Line__ChannelAccessToken" "<チャネルアクセストークン>"
dotnet user-secrets set "HuggingFace__ApiKey" "hf_xxxxxxxxxxxxxxxxx"
dotnet user-secrets set "App__PublicBaseUrl" "https://<トンネル>.devtunnels.ms"
cd ..
dotnet run --project LineHfBot --urls http://localhost:8080
```

`--urls` でトンネルと同じ 8080 に揃えています。

> **Windows なら一発。** `scripts/run.ps1 -Port 8081 -StartTunnel -Rebuild` でイメージのビルド・ホストの
> ルート CA を `certs/` へエクスポート（下記参照）・Dev Tunnel の起動・コンテナ起動をまとめて実行します。

### 起動確認

```bash
curl http://localhost:8080/health      # -> {"status":"ok"}
```

---

## 4. Webhook を設定して話しかける

この手順は Docker Hub ガイドと同じです。
[LINE の Webhook を向ける](docker-hub.ja.md#4-line-の-webhook-を向ける)と
[アプリで確認](docker-hub.ja.md#5-アプリで確認)に従ってください。

---

## 企業の TLS 検査プロキシ環境

TLS を検査するプロキシ下では、ビルド/実行時に組織のルート CA が必要です。ビルド前にルート CA の `*.crt`
ファイルを `certs/` に置いてください。`Dockerfile` がそれらを信頼します（gitignore 済みで公開されません）。
（`scripts/run.ps1` はホストのルート CA を `certs/` へ自動エクスポートします。）

---

## 自分でイメージを公開する

フォークなどから自分のイメージをレジストリへビルド・push する方法は、Docker Hub ガイドの
[自分でイメージを公開する（CI/CD）](docker-hub.ja.md#付録--自分でイメージを公開するcicd)付録を参照してください。
