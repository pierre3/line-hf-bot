# Deploy: Azure Container Apps

English | [日本語](azure-container-apps.ja.md)

Run the published Docker Hub image on **Azure Container Apps (ACA)** — a managed container host that gives you an HTTPS endpoint out of the box, which is exactly what LINE needs.

> **Important — single instance only.** Conversation history and generated media are kept **in memory** (see [Limitations](../../README.md#limitations)). ACA's default HTTP scale rule is min 0 / max 10; you **must** pin it to **`--min-replicas 1 --max-replicas 1`**:
> - `max-replicas 1` — a second replica would have its own separate memory, so state and `/media/{id}` URLs would break across replicas.
> - `min-replicas 1` — with min 0 the app scales to zero when idle, losing all state and delaying webhook delivery on cold start.

Prerequisites: an Azure subscription, the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), the image published to Docker Hub ([guide](docker-hub.md)), and your LINE + Hugging Face credentials.

---

## 1. One-time CLI setup

```bash
az login
az extension add --name containerapp --upgrade
az provider register --namespace Microsoft.App --wait
az provider register --namespace Microsoft.OperationalInsights --wait
```

## 2. Create the resource group and environment

```bash
RG=line-hf-bot-rg
LOC=japaneast
ENV=line-hf-bot-env

az group create --name $RG --location $LOC
az containerapp env create --name $ENV --resource-group $RG --location $LOC
```

## 3. Create the container app

Store tokens as **secrets** and reference them from env vars with `secretref:`. Leave `App__PublicBaseUrl` out for now — you need the app's FQDN first (set in the next step).

```bash
APP=line-hf-bot
IMAGE=<your-dockerhub-user>/line-hf-bot:latest      # or a pinned tag like :1.0.0

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
      line-secret="<CHANNEL_SECRET>" \
      line-token="<CHANNEL_ACCESS_TOKEN>" \
      hf-key="<HF_TOKEN>" \
  --env-vars \
      Line__ChannelSecret=secretref:line-secret \
      Line__ChannelAccessToken=secretref:line-token \
      HuggingFace__ApiKey=secretref:hf-key \
      App__Locale=en \
      App__VideoEnabled=false
```

> The image is pulled from public Docker Hub, so no registry credentials are needed. For a **private** repo, add `--registry-server docker.io --registry-username <user> --registry-password <token>`.

## 4. Set `App__PublicBaseUrl` to the app's own URL

Get the assigned HTTPS FQDN, then feed it back in (this creates a new revision):

```bash
FQDN=$(az containerapp show --name $APP --resource-group $RG \
  --query properties.configuration.ingress.fqdn -o tsv)
echo "https://$FQDN"

az containerapp update --name $APP --resource-group $RG \
  --set-env-vars App__PublicBaseUrl="https://$FQDN"
```

Verify it's up:

```bash
curl "https://$FQDN/health"      # -> {"status":"ok"}
```

## 5. Point LINE at the webhook

In the [LINE Developers console](https://developers.line.biz/) enable **Use webhook** and turn off auto-reply, then set the webhook URL to `https://<FQDN>/webhook` (see the [LINE setup walkthrough](docker-hub.md#step-3--point-line-at-the-webhook) for the `line` CLI shortcut and the in-app verification steps).

---

## Updating

- **New image version:** `az containerapp update --name $APP --resource-group $RG --image <your-dockerhub-user>/line-hf-bot:1.1.0`
- **Change a setting:** `az containerapp update --name $APP --resource-group $RG --set-env-vars App__VideoEnabled=true`
- **Rotate a secret:** `az containerapp secret set --name $APP --resource-group $RG --secrets hf-key="<NEW>"` then restart the active revision.

## Notes & costs

- ACA bills for vCPU/memory while replicas run. With `min-replicas 1` the app runs 24/7 (no scale-to-zero) — that is required here, so expect a small always-on cost.
- The FQDN is stable for the life of the app, so the LINE webhook and `App__PublicBaseUrl` don't need to change on redeploys.
- Logs: `az containerapp logs show --name $APP --resource-group $RG --follow`.
- Tear down everything: `az group delete --name $RG`.
