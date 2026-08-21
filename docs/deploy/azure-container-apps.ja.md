# デプロイ: Azure Container Apps

[English](azure-container-apps.md) | 日本語

Docker Hub に公開したイメージを **Azure Container Apps (ACA)** で動かします。マネージドなコンテナホストで、最初から HTTPS エンドポイントが付く — LINE が求めるものそのものです。

> **重要 — 単一インスタンス限定。** 会話履歴と生成メディアは**メモリ内**に保持されます（[制限事項](../../README.ja.md#制限事項)参照）。ACA の既定 HTTP スケールは min 0 / max 10 なので、**必ず** **`--min-replicas 1 --max-replicas 1`** に固定してください。
> - `max-replicas 1` — 2 個目のレプリカは別々のメモリを持つため、状態や `/media/{id}` URL がレプリカ間で壊れます。
> - `min-replicas 1` — min 0 だとアイドル時にゼロスケールして全状態を失い、コールドスタートで Webhook 配信が遅れます。

## ワンクリックデプロイ（Deploy to Azure ボタン）

一番手軽な方法 — CLI もクローンも不要。ボタンを押すと Azure ポータルがフォーム付きで開くので、3 つの認証情報を入力してデプロイするだけです。

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fpierre3%2Fline-hf-bot%2Fmain%2Finfra%2Fazuredeploy.json/createUIDefinitionUri/https%3A%2F%2Fraw.githubusercontent.com%2Fpierre3%2Fline-hf-bot%2Fmain%2Finfra%2FcreateUiDefinition.json)

- **入力するもの:** LINE のチャネルシークレット、チャネルアクセストークン、Hugging Face トークン。任意で言語（`en`/`ja`）、vision/動画のオン・オフ、CPU/メモリも選べます。
- **作られるもの**（選んだリソースグループ内）: Log Analytics ワークスペース、Container Apps マネージド環境、Container App 本体。**最大レプリカ数は常に 1**（メモリ内状態はレプリカ間で分割できないため）。**最小レプリカ数は既定 1（常時起動・推奨）**。試用なら **0**（アイドル時にゼロスケール）も選べます — 安いが、アイドルのたびにメモリ内状態（履歴・生成メディア）が消え、次のメッセージでコールドスタートします。`App__PublicBaseUrl` はアプリ自身の HTTPS URL に自動設定されるので、FQDN の二段階設定は不要です。
- **完了後:** Webhook URL は**デプロイの「出力（Outputs）」タブ**（リソースグループ →「デプロイ」→ 該当デプロイ → 出力、`lineWebhookUrl`）に表示されます。**完了画面には出ません。** Container App の **Application Url** に `/webhook` を付けて組み立てても OK。これを LINE に登録し（下記[LINE の Webhook を向ける](#5-line-の-webhook-を向ける)）、`curl https://<fqdn>/health` → `{"status":"ok"}` で確認します。

> **コスト:** 最小レプリカ数 1 だと 24 時間稼働（ゼロスケールしない）なので、小額でも常時課金が発生します。0 にすればアイドル課金は避けられますが、コールドスタートと状態消失が代償です。不要になったらリソースグループを削除してください（ポータル、または `az group delete --name <rg>`）。

**うまく動かないとき:**
- **LINE の Verify が 401** — `Line__ChannelSecret` の値が間違っている（またはアクセストークンと取り違え）。ポータルの環境変数は secret 参照だと「値」欄に *secret 名*（例 `line-channel-secret`）を表示しますが、これは参照の正常表示で値そのものではありません。実値は `az containerapp secret show -n line-hf-bot -g <rg> --secret-name line-channel-secret` で確認し、直して（`az containerapp secret set ...`）アクティブリビジョンを再起動します。
- **チャットで「エラーが起きました」** — チャットモデルが有効プロバイダで配信されていません。[チャット トラブルシュート](../../README.ja.md#チャット-トラブルシュート)参照（`GET /v1/models` で現行モデルを確認し `HuggingFace__ChatModel` を設定）。

テンプレートは [`infra/`](../../infra/) にあります: `main.bicep`（元）→ `azuredeploy.json`（ボタンが読み込むファイル）と、フォーム定義の `createUiDefinition.json`。

---

## 手動デプロイ（Azure CLI）

CLI を使いたい、またはフォーム以上に細かく調整したい場合は、以下の手順で同じ構成を手作業で作れます。

事前準備: Azure サブスクリプション、[Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)、Docker Hub に公開済みのイメージ（[ガイド](docker-hub.ja.md)）、LINE と Hugging Face の資格情報。

### 1. CLI の初回セットアップ

```bash
az login
az extension add --name containerapp --upgrade
az provider register --namespace Microsoft.App --wait
az provider register --namespace Microsoft.OperationalInsights --wait
```

### 2. リソースグループと環境の作成

```bash
RG=line-hf-bot-rg
LOC=japaneast
ENV=line-hf-bot-env

az group create --name $RG --location $LOC
az containerapp env create --name $ENV --resource-group $RG --location $LOC
```

### 3. Container App の作成

トークンは**シークレット**として保存し、env var から `secretref:` で参照します。`App__PublicBaseUrl` はまだ入れません — 先にアプリの FQDN が必要です（次の手順で設定）。

```bash
APP=line-hf-bot
IMAGE=pierre3/line-hf-bot:latest      # または :1.0.0 のような固定タグ

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

### 4. `App__PublicBaseUrl` をアプリ自身の URL に設定

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

### 5. LINE の Webhook を向ける

[LINE Developers コンソール](https://developers.line.biz/)で **Webhook の利用**をオン、応答メッセージをオフにし、Webhook URL を `https://<FQDN>/webhook` に設定します（`line` CLI のショートカットとアプリ内での動作確認手順は [LINE 動作確認の手順](docker-hub.ja.md#4-line-の-webhook-を向ける) を参照）。

---

## 更新のしかた

- **イメージのバージョン更新:** `az containerapp update --name $APP --resource-group $RG --image pierre3/line-hf-bot:1.1.0`
- **設定の変更:** `az containerapp update --name $APP --resource-group $RG --set-env-vars App__VideoEnabled=true`
- **シークレットのローテーション:** `az containerapp secret set --name $APP --resource-group $RG --secrets hf-key="<新しい値>"` の後、アクティブなリビジョンを再起動。

## メモとコスト

- ACA はレプリカ稼働中の vCPU/メモリに課金します。`min-replicas 1` だと 24 時間稼働（ゼロスケールしない）— 本アプリでは必須なので、小額でも常時コストが発生します。
- FQDN はアプリが存在する限り安定なので、再デプロイしても LINE Webhook と `App__PublicBaseUrl` を変える必要はありません。
- ログ: `az containerapp logs show --name $APP --resource-group $RG --follow`。
- 一括削除: `az group delete --name $RG`。
