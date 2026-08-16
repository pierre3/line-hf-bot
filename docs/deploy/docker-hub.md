# Deploy: Docker Hub (CI/CD) & LINE setup walkthrough

English | [日本語](docker-hub.ja.md)

This guide covers two things:

1. **Publishing** the image to Docker Hub — automatically via GitHub Actions, or by hand.
2. **Running** the published image and verifying it end-to-end in the LINE app.

Related: [Azure Container Apps deployment](azure-container-apps.md).

---

## 1. Publish to Docker Hub

### Option A — Automated (GitHub Actions, recommended)

The repo ships two workflows:

- `.github/workflows/ci.yml` — builds and tests (and does a no-push Docker build) on every push/PR to `main`.
- `.github/workflows/release.yml` — builds a **multi-arch** image (`linux/amd64`, `linux/arm64`) and pushes it to Docker Hub when you push a **version tag** (`v*`).

**One-time setup**

1. Create a Docker Hub access token: Docker Hub → *Account Settings → Personal access tokens → Generate new token* (scope **Read & Write**).
2. In the GitHub repo, add two secrets (*Settings → Secrets and variables → Actions → New repository secret*):
   - `DOCKERHUB_USERNAME` — your Docker Hub account/namespace (this is also the image namespace).
   - `DOCKERHUB_TOKEN` — the access token from step 1.

**Release**

```bash
git tag v1.0.0
git push origin v1.0.0
```

The workflow publishes:

- `<DOCKERHUB_USERNAME>/line-hf-bot:1.0.0`
- `<DOCKERHUB_USERNAME>/line-hf-bot:1.0` (major.minor)
- `<DOCKERHUB_USERNAME>/line-hf-bot:latest`

Pre-release tags such as `v1.0.0-rc1` are published but do **not** move `latest`.

### Option B — Manual

```bash
docker build -t <your-user>/line-hf-bot:1.0.0 -t <your-user>/line-hf-bot:latest .
docker login
docker push <your-user>/line-hf-bot:1.0.0
docker push <your-user>/line-hf-bot:latest
```

> Behind a corporate TLS-inspecting proxy? Drop your root CA `*.crt` files into `certs/` before building; the Dockerfile trusts them (they are gitignored and never published).

---

## 2. Run the image & verify in LINE

You can run the published image anywhere Docker runs — your PC, a VM, or a container host. For a fully managed option see [Azure Container Apps](azure-container-apps.md).

### Prerequisites

- A **LINE Messaging API channel** — its **Channel secret** and a long-lived **Channel access token**.
- A **Hugging Face token** with the **Inference Providers** permission.
- A **public HTTPS URL** for the app. Either a tunnel (Dev Tunnels / ngrok / Cloudflare Tunnel) pointing at your local port, or a cloud host that gives you an HTTPS endpoint. LINE requires HTTPS for both the webhook and the image URLs the bot returns.

### Step 1 — Create the `.env`

Start from [`.env.example`](../../.env.example) and fill in at least:

```dotenv
Line__ChannelSecret=...
Line__ChannelAccessToken=...
HuggingFace__ApiKey=hf_...
App__PublicBaseUrl=https://<your-public-host>   # no trailing slash
```

`App__PublicBaseUrl` must be the **public** HTTPS base at which this app is reachable — the bot builds `/media/{id}` image URLs from it, and LINE fetches those.

### Step 2 — Run

```bash
docker run --env-file .env -p 8080:8080 <your-user>/line-hf-bot:latest
```

Sanity check the health endpoint:

```bash
curl http://localhost:8080/health      # -> {"status":"ok"}
```

If you are exposing a local run through a tunnel, start it now and use that HTTPS URL as `App__PublicBaseUrl` (restart the container if you changed `.env`):

```bash
devtunnel host -p 8080 --allow-anonymous
```

### Step 3 — Point LINE at the webhook

In the [LINE Developers console](https://developers.line.biz/): open your channel → *Messaging API*, enable **Use webhook**, and turn **off** "Auto-reply messages" / "Greeting messages".

Set the webhook URL to `https://<your-public-host>/webhook`. The `line` CLI makes this easy:

```bash
dotnet tool install -g Line.OpenApi.Tools
line config set default --token "YOUR_CHANNEL_ACCESS_TOKEN"
line webhook set-endpoint --url "https://<your-public-host>/webhook"
line webhook test-endpoint          # expect success / 200
```

### Step 4 — Verify in the app

1. Add the bot as a friend (QR code in the console → *Messaging API*).
2. Send a plain message → you should get a **chat** reply.
3. Send `/image a cat on a skateboard` → you should get a generated **image** with 🔄 / ✏️ / 💬 buttons.
4. Tap **✏️ Edit** (or send a photo), then send an edit instruction → an **edited image** (uses the paid fal-ai provider).
5. Optional: if `App__VideoEnabled=true`, send `/video ...` → a **video** (fal-ai, paid and slow).

If chat works but image editing / video fail with an error, check that your HF token has **Inference Providers** permission **and credits** — image editing and video use the paid **fal-ai** provider.

### Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| Webhook test fails | `App__PublicBaseUrl` / webhook URL not HTTPS or not reachable; container not running |
| Chat replies but no image shows | `App__PublicBaseUrl` wrong or not publicly reachable (LINE can't fetch `/media/{id}`) |
| "error" on edit/video | HF token missing Inference Providers permission or credits (fal-ai is paid) |
| No rich menu | `App__RichMenuEnabled=false`, or the channel access token lacks rich-menu scope |

> **Note on state:** conversation history and generated media are kept **in memory** and are lost on restart. Run a **single instance** — see [Limitations](../../README.md#limitations).
