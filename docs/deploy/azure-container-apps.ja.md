# デプロイ: Azure Container Apps

[English](azure-container-apps.md) | 日本語

Docker Hub に公開したイメージを **Azure Container Apps (ACA)** で動かします。マネージドなコンテナホストで、最初から HTTPS エンドポイントが付く — LINE が求めるものそのものです。

> **重要 — 単一インスタンス限定。** 会話履歴と生成メディアは**メモリ内**に保持されます（[制限事項](../../README.ja.md#制限事項)参照）。ACA の既定 HTTP スケールは min 0 / max 10 なので、**必ず** **`--min-replicas 1 --max-replicas 1`** に固定してください。
> - `max-replicas 1` — 2 個目のレプリカは別々のメモリを持つため、状態や `/media/{id}` URL がレプリカ間で壊れます。
> - `min-replicas 1` — min 0 だとアイドル時にゼロスケールして全状態を失い、コールドスタートで Webhook 配信が遅れます。

事前準備: Azure サブスクリプション、[Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)、Docker Hub に公開済みのイメージ（[ガイド](docker-hub.ja.md)）、LINE と Hugging Face の資格情報。

---

## 1. CLI の初回セットアップ

```bash
az login
az extension add --name containerapp --upgrade
az provider register --namespace Microsoft.App --wait
az provider register --namespace Microsoft.OperationalInsights --wait
```

## 2. リソースグループと環境の作成

```bash
RG=line-hf-bot-rg
LOC=japaneast
ENV=line-hf-bot-env

az group create --name $RG --location $LOC
az containerapp env create --name $ENV --resource-group $RG --location $LOC
```

## 3. Container App の作成

トークンは**シークレット**として保存し、env var から `secretref:` で参照します。`App__PublicBaseUrl` はまだ入れません — 先にアプリの FQDN が必要です（次の手順で設定）。

```bash
APP=line-hf-bot
IMAGE=<Docker Hub ユーザー名>/line-hf-bot:latest      # または :1.0.0 のような固定タグ

az containerapp create \
  --name $APP \
  --resource-group $RG \
  --environment $ENV \
  --image "$IMAGE" \
  --target-port 8080 \
  --ingress external \
  --min-replicas 1 \
  --max-replicas 1 \
  --cpu 0.5 --memory 1.0Gi \
  --secrets \
      line-secret="<チャネルシークレット>" \
      line-token="<チャネルアクセストークン>" \
      hf-key="<HF トークン>" \
  --env-vars \
      Line__ChannelSecret=secretref:line-secret \
      Line__ChannelAccessToken=secretref:line-token \
      HuggingFace__ApiKey=secretref:hf-key \
      App__Locale=ja \
      App__VideoEnabled=false
```

> イメージは公開 Docker Hub から取得するのでレジストリ認証は不要です。**非公開**リポジトリの場合は `--registry-server docker.io --registry-username <user> --registry-password <token>` を追加します。

## 4. `App__PublicBaseUrl` をアプリ自身の URL に設定

割り当てられた HTTPS FQDN を取得し、それを設定し直します（新しいリビジョンが作られます）。

```bash
FQDN=$(az containerapp show --name $APP --resource-group $RG \
  --query properties.configuration.ingress.fqdn -o tsv)
echo "https://$FQDN"

az containerapp update --name $APP --resource-group $RG \
  --set-env-vars App__PublicBaseUrl="https://$FQDN"
```

起動確認:

```bash
curl "https://$FQDN/health"      # -> {"status":"ok"}
```

## 5. LINE の Webhook を向ける

[LINE Developers コンソール](https://developers.line.biz/)で **Webhook の利用**をオン、応答メッセージをオフにし、Webhook URL を `https://<FQDN>/webhook` に設定します（`line` CLI のショートカットとアプリ内での動作確認手順は [LINE 動作確認の手順](docker-hub.ja.md#手順3--line-の-webhook-を向ける) を参照）。

---

## 更新のしかた

- **イメージのバージョン更新:** `az containerapp update --name $APP --resource-group $RG --image <Docker Hub ユーザー名>/line-hf-bot:1.1.0`
- **設定の変更:** `az containerapp update --name $APP --resource-group $RG --set-env-vars App__VideoEnabled=true`
- **シークレットのローテーション:** `az containerapp secret set --name $APP --resource-group $RG --secrets hf-key="<新しい値>"` の後、アクティブなリビジョンを再起動。

## メモとコスト

- ACA はレプリカ稼働中の vCPU/メモリに課金します。`min-replicas 1` だと 24 時間稼働（ゼロスケールしない）— 本アプリでは必須なので、小額でも常時コストが発生します。
- FQDN はアプリが存在する限り安定なので、再デプロイしても LINE Webhook と `App__PublicBaseUrl` を変える必要はありません。
- ログ: `az containerapp logs show --name $APP --resource-group $RG --follow`。
- 一括削除: `az group delete --name $RG`。
