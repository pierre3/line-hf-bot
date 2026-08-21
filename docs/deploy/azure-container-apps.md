# Deploy: Azure Container Apps

English | [日本語](azure-container-apps.ja.md)

Run the published Docker Hub image on **Azure Container Apps (ACA)** — a managed container host that gives you an HTTPS endpoint out of the box, which is exactly what LINE needs.

> **Important — single instance only.** Conversation history and generated media are kept **in memory** (see [Limitations](../../README.md#limitations)). ACA's default HTTP scale rule is min 0 / max 10; you **must** pin it to **`--min-replicas 1 --max-replicas 1`**:
> - `max-replicas 1` — a second replica would have its own separate memory, so state and `/media/{id}` URLs would break across replicas.
> - `min-replicas 1` — with min 0 the app scales to zero when idle, losing all state and delaying webhook delivery on cold start.

## One-click deploy (Deploy to Azure button)

The fastest path — no CLI, no clone. The button opens the Azure portal with a form; fill in three credentials and deploy.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fpierre3%2Fline-hf-bot%2Fmain%2Finfra%2Fazuredeploy.json/createUIDefinitionUri/https%3A%2F%2Fraw.githubusercontent.com%2Fpierre3%2Fline-hf-bot%2Fmain%2Finfra%2FcreateUiDefinition.json)

- **What you enter:** LINE channel secret, LINE channel access token, and your Hugging Face token. Optionally set the language (`en`/`ja`), toggle vision/video, and pick CPU/memory.
- **What it provisions** (into the resource group you choose): a Log Analytics workspace, a Container Apps managed environment, and the Container App — pinned to a single **always-on** replica (`min = max = 1`, required by the in-memory design). `App__PublicBaseUrl` is set automatically to the app's own HTTPS URL, so there's no two-step FQDN step.
- **After it finishes:** open the deployment's **Outputs** — `lineWebhookUrl` is the URL to register in LINE. Continue from [step 5](#5-point-line-at-the-webhook) to wire up the webhook, and verify with `curl https://<fqdn>/health` → `{"status":"ok"}`.

> **Cost:** the single replica runs 24/7 (no scale-to-zero), so expect a small always-on charge. Tear everything down by deleting the resource group (portal, or `az group delete --name <rg>`).

**If it doesn't work:**
- **LINE "Verify" returns 401** — the `Line__ChannelSecret` value is wrong (or was swapped with the access token). Note the portal shows the *secret name* (e.g. `line-channel-secret`) for secret-backed env vars — that's the normal display of a reference, not the value. Read the actual value with `az containerapp secret show -n line-hf-bot -g <rg> --secret-name line-channel-secret`, fix it (`az containerapp secret set ...`), then restart the active revision.
- **Chatting replies "Something went wrong"** — the chat model isn't served by your enabled Inference Providers. See [Chat troubleshooting](../../README.md#chat-troubleshooting) (list current models via `GET /v1/models` and set `HuggingFace__ChatModel`).

The template lives in [`infra/`](../../infra/): `main.bicep` (source) → `azuredeploy.json` (what the button loads), plus `createUiDefinition.json` for the form.

---

## Manual deploy (Azure CLI)

Prefer the CLI, or want to customize beyond the form? The steps below provision the same thing by hand.

Prerequisites: an Azure subscription, the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), the image published to Docker Hub ([guide](docker-hub.md)), and your LINE + Hugging Face credentials.

### 1. One-time CLI setup

```bash
az login
az extension add --name containerapp --upgrade
az provider register --namespace Microsoft.App --wait
az provider register --namespace Microsoft.OperationalInsights --wait
```

### 2. Create the resource group and environment

```bash
RG=line-hf-bot-rg
LOC=japaneast
ENV=line-hf-bot-env

az group create --name $RG --location $LOC
az containerapp env create --name $ENV --resource-group $RG --location $LOC
```

### 3. Create the container app

Store tokens as **secrets** and reference them from env vars with `secretref:`. Leave `App__PublicBaseUrl` out for now — you need the app's FQDN first (set in the next step).

```bash
APP=line-hf-bot
IMAGE=pierre3/line-hf-bot:latest      # or a pinned tag like :1.0.0

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

### 4. Set `App__PublicBaseUrl` to the app's own URL

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

### 5. Point LINE at the webhook

In the [LINE Developers console](https://developers.line.biz/) enable **Use webhook** and turn off auto-reply, then set the webhook URL to `https://<FQDN>/webhook` (see the [LINE setup walkthrough](docker-hub.md#4-point-line-at-the-webhook) for the `line` CLI shortcut and the in-app verification steps).

---

## Updating

- **New image version:** `az containerapp update --name $APP --resource-group $RG --image pierre3/line-hf-bot:1.1.0`
- **Change a setting:** `az containerapp update --name $APP --resource-group $RG --set-env-vars App__VideoEnabled=true`
- **Rotate a secret:** `az containerapp secret set --name $APP --resource-group $RG --secrets hf-key="<NEW>"` then restart the active revision.

## Notes & costs

- ACA bills for vCPU/memory while replicas run. With `min-replicas 1` the app runs 24/7 (no scale-to-zero) — that is required here, so expect a small always-on cost.
- The FQDN is stable for the life of the app, so the LINE webhook and `App__PublicBaseUrl` don't need to change on redeploys.
- Logs: `az containerapp logs show --name $APP --resource-group $RG --follow`.
- Tear down everything: `az group delete --name $RG`.
