# Deploy: Run from Docker Hub (& LINE setup)

English | [日本語](docker-hub.ja.md)

Pull the published image from Docker Hub, configure it with a `.env` file, run it, and connect it to LINE.
This is the normal way to run the bot — you do **not** need the source code or the .NET SDK.

Related: [Azure Container Apps](azure-container-apps.md) for a managed host · [Publishing your own image](#appendix--publish-your-own-image-cicd) (maintainers).

---

## Prerequisites

- A **LINE Messaging API channel** — its **Channel secret** and a long-lived **Channel access token**
  (LINE Developers console → your channel → *Messaging API* / *Basic settings*).
- A **Hugging Face token** with the **Inference Providers** permission. All generation draws down your
  Inference Providers **credits** (there's a free monthly allowance); image editing and video use the fal-ai
  provider, which costs much more per call than hf-inference and eats those credits quickly.
- A **public HTTPS URL** for the app. Either a tunnel (Dev Tunnels / ngrok / Cloudflare Tunnel) pointing at
  your local port, or a cloud host that gives you an HTTPS endpoint. LINE requires HTTPS for both the webhook
  and the media URLs the bot returns.
- **Docker**.

---

## 1. Expose a public HTTPS URL (get it first)

The app must be reachable from the internet over HTTPS — LINE needs it for both the webhook and the
`/media/{id}` URLs the bot returns. **Do this first**, because the URL becomes `App__PublicBaseUrl` in the
next step.

If you're running locally, start a tunnel and keep it running:

```bash
devtunnel host -p 8080 --allow-anonymous
```

Copy the `https://…devtunnels.ms` URL it prints. (ngrok or Cloudflare Tunnel work too.) If you're hosting in
the cloud instead, use the platform's HTTPS endpoint — see the
[Azure Container Apps guide](azure-container-apps.md).

---

## 2. Create the `.env` file

The container is configured entirely through **environment variables**. The easiest way to supply them is an
`.env` file passed with `--env-file` (or `env_file:` in Docker Compose).

Setting names use the pattern `Section__Key` — note the **double underscore** (`__`) between the section and
the key (e.g. section `App`, key `PublicBaseUrl` → `App__PublicBaseUrl`).

Create a file named `.env`. At minimum, fill in these four (paste the tunnel URL from step 1 into
`App__PublicBaseUrl`):

```dotenv
# Required
Line__ChannelSecret=<your channel secret>
Line__ChannelAccessToken=<your channel access token>
HuggingFace__ApiKey=hf_xxxxxxxxxxxxxxxxx
App__PublicBaseUrl=https://<your-public-host>      # no trailing slash
```

`App__PublicBaseUrl` must be the **public** HTTPS base at which this app is reachable from the internet — the
bot builds `/media/{id}` image/video URLs from it, and LINE fetches those. (When you use a tunnel, this is the
tunnel's HTTPS URL; on a cloud host, it's the app's HTTPS endpoint.)

Everything else has a sensible default and is optional. Add only what you want to change.

### Parameter reference

**Required**

| Variable | Description |
| --- | --- |
| `Line__ChannelSecret` | LINE channel secret. Used to verify the webhook signature. |
| `Line__ChannelAccessToken` | LINE long-lived channel access token. Used to send messages and provision the rich menu. |
| `HuggingFace__ApiKey` | Hugging Face token with **Inference Providers** permission (and credits, for editing/video). |
| `App__PublicBaseUrl` | Public **HTTPS** base URL where this app is reachable. No trailing slash. Required so LINE can fetch generated media. |

**Models & providers** (defaults work out of the box; change to use different models/providers)

| Variable | Default | Description |
| --- | --- | --- |
| `HuggingFace__ChatModel` | `Qwen/Qwen2.5-72B-Instruct` | Chat model (non-gated). Must be served by a provider your token has enabled; provider catalogs change, so if chat fails with `model_not_supported`, list current models with `curl https://router.huggingface.co/v1/models -H "Authorization: Bearer <token>"` and switch. Gated models need you to accept their license on HF first. |
| `HuggingFace__ChatEndpoint` | `https://router.huggingface.co` | Chat base URL. Semantic Kernel appends `/v1/chat/completions`, so do **not** include `/v1`. |
| `HuggingFace__ImageModel` | `stabilityai/stable-diffusion-3-medium-diffusers` | Text-to-image model. |
| `HuggingFace__ImageEndpoint` | `https://router.huggingface.co/hf-inference/models/{model}` | Text-to-image endpoint; `{model}` is replaced with `ImageModel`. |
| `HuggingFace__ImageEditModel` | `fal-ai/qwen-image-edit` | Image-to-image (✏️ edit) model. **fal-ai costs more credits per call.** |
| `HuggingFace__ImageEditEndpoint` | `https://router.huggingface.co/fal-ai/{model}?_subdomain=queue` | fal async-queue submit endpoint for editing; `{model}` → `ImageEditModel`. |
| `HuggingFace__VideoModel` | `fal-ai/wan/v2.2-5b/text-to-video` | Text-to-video model. **fal-ai is credit-heavy.** |
| `HuggingFace__VideoEndpoint` | `https://router.huggingface.co/fal-ai/{model}?_subdomain=queue` | fal async-queue submit endpoint for video; `{model}` → `VideoModel`. |
| `HuggingFace__ImageToVideoModel` | `fal-ai/wan/v2.2-a14b/image-to-video` | Image-to-video (🎬 Make a video) model; lighter alternative `fal-ai/wan-i2v`. **fal-ai is credit-heavy**; A14B costs more than the 5B text-to-video default. |
| `HuggingFace__ImageToVideoEndpoint` | `https://router.huggingface.co/fal-ai/{model}?_subdomain=queue` | fal async-queue submit endpoint for image-to-video; `{model}` → `ImageToVideoModel`. |
| `HuggingFace__VisionModel` | `Qwen/Qwen2.5-VL-72B-Instruct:ovhcloud` | Vision Q&A model for sent photos (💬 Ask about this image). **Pin the provider** (`model:provider`) and enable it in HF settings — `auto` fails with `model_not_supported`. Uses chat-level HF credits (not fal). |
| `HuggingFace__VisionEndpoint` | `https://router.huggingface.co/v1/chat/completions` | OpenAI-compatible chat-completions **full URL** for vision (includes `/v1/chat/completions`, unlike `ChatEndpoint`). |
| `HuggingFace__MediaRefetchAllowedHosts` | `fal.media;replicate.delivery` | Hosts allowed when the app re-fetches media from a provider URL. Label-boundary match; **empty = deny all**. |

> **hf-inference does not serve image-to-image, text-to-video, or image-to-video.** Those default to the
> **fal-ai** provider (which costs more credits per call). If you point
> `VideoEndpoint`/`ImageEditEndpoint`/`ImageToVideoEndpoint` at `hf-inference`, you'll get
> `400 "Model not supported by provider hf-inference"`.

**App behavior**

| Variable | Default | Description |
| --- | --- | --- |
| `App__VideoEnabled` | `false` | Enable video: the `/video` command (text-to-video) **and** 🎬 Make a video (image-to-video). Both run on the **credit-heavy, slow** fal-ai provider; set `true` to allow them. |
| `App__VisionEnabled` | `true` | Vision Q&A on sent photos and generated images. On: a sent photo offers ✏️ Edit / 💬 Ask about this image, and image results add a 💬 Ask button. Off: a sent photo goes straight to editing (no vision UI). |
| `App__VisionMaxTurns` | `8` | Max Q&A turns kept in a conversational vision session (min 1). Each follow-up resends the image + prior turns, so credit cost grows with turns — this caps it. |
| `App__Locale` | `en` | Language of user-facing text and the rich menu (`en` or `ja`). |
| `App__RichMenuEnabled` | `true` | Provision the mode-switcher rich menu on startup (idempotent). `false` runs without it. |
| `App__MediaTtlMinutes` | `10` | How long generated media is kept in memory and served at `/media/{id}`. |
| `Line__MaxIncomingImageBytes` | `10485760` | Max size (bytes) of a user-sent photo the bot will download to edit. Default 10 MB. |
| `Line__ContentFetchTimeoutSeconds` | `30` | Timeout for downloading a user-sent photo. |

**Optional tuning**

| Variable | Default | Description |
| --- | --- | --- |
| `HOST_PORT` | `8080` | **Docker Compose only** — the host port to expose (the app inside always listens on 8080). Not read by the app itself; with `docker run` use `-p` instead. |
| `HuggingFace__ChatTimeoutSeconds` | `60` | Chat request timeout. |
| `HuggingFace__ImageTimeoutSeconds` | `120` | Text-to-image timeout. |
| `HuggingFace__ImageEditTimeoutSeconds` | `120` | Image edit timeout. |
| `HuggingFace__VideoTimeoutSeconds` | `300` | Video timeout (text-to-video and image-to-video are slow — keep this generous). |
| `HuggingFace__VisionTimeoutSeconds` | `120` | Vision Q&A timeout (a cold first request can be slow). |
| `Queue__Capacity` | `100` | Max queued jobs; when full the user is told the bot is busy. |
| `Queue__Workers` | `2` | Number of parallel workers processing the queue. |
| `Chat__MaxHistory` | `20` | Conversation turns kept per user (in memory). |

> A ready-to-edit template with every key is in the repo's [`.env.example`](../../.env.example).

---

## 3. Pull and run the image

The image is published on Docker Hub as [`pierre3/line-hf-bot`](https://hub.docker.com/r/pierre3/line-hf-bot).
Use `:latest` or pin a version such as `:1.0.0`. (If you run your own build from a fork, substitute your own
Docker Hub namespace.)

### With `docker run`

```bash
docker pull pierre3/line-hf-bot:latest
docker run --env-file .env -p 8080:8080 pierre3/line-hf-bot:latest
```

To expose it on a different host port (e.g. 8081), change the **left** side of `-p`: `-p 8081:8080`.

### With Docker Compose

Create a `compose.yaml` next to your `.env` that uses the published image (no build needed):

```yaml
services:
  line-hf-bot:
    image: pierre3/line-hf-bot:latest
    container_name: line-hf-bot
    ports:
      - "${HOST_PORT:-8080}:8080"   # set HOST_PORT in .env to change the host port
    env_file:
      - .env
    restart: unless-stopped
```

```bash
docker compose pull
docker compose up -d
```

### Verify it started

```bash
curl http://localhost:8080/health      # -> {"status":"ok"}
```

> **State is in memory.** Conversation history and generated media live only in the running container and are
> lost on restart. Run a **single instance** — see [Limitations](../../README.md#limitations).

---

## 4. Point LINE at the webhook

In the [LINE Developers console](https://developers.line.biz/): open your channel → *Messaging API*, enable
**Use webhook**, and turn **off** "Auto-reply messages" / "Greeting messages".

Set the webhook URL to `https://<your-public-host>/webhook`. The `line` CLI makes this easy:

```bash
dotnet tool install -g Line.OpenApi.Tools
line config set default --token "YOUR_CHANNEL_ACCESS_TOKEN"
line webhook set-endpoint --url "https://<your-public-host>/webhook"
line webhook test-endpoint          # expect success / 200
```

---

## 5. Verify in the app

1. Add the bot as a friend (QR code in the console → *Messaging API*).
2. Send a plain message → you should get a **chat** reply.
3. Send `/image a cat on a skateboard` → a generated **image** with 🔄 / ✏️ / 💬 buttons.
4. Tap **✏️ Edit** (or send a photo), then send an edit instruction → an **edited image** (fal-ai, credit-heavy).
5. Send a photo and tap **💬 Ask about this image**, then ask a question → a text **answer** (vision Q&A; on by default).
6. Optional: if `App__VideoEnabled=true`, send `/video a running cat` → a **video**, or tap **🎬 Make a video** on an image → an animated clip (both fal-ai, credit-heavy and slow).

---

## Updating to a new image version

```bash
# docker run
docker pull pierre3/line-hf-bot:latest
docker rm -f line-hf-bot && docker run --env-file .env -p 8080:8080 pierre3/line-hf-bot:latest

# docker compose
docker compose pull && docker compose up -d
```

Changing a value in `.env` only takes effect after you recreate the container (`.env` is read at startup, not
build time) — no rebuild needed when using a pulled image.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| Webhook test fails | `App__PublicBaseUrl` / webhook URL not HTTPS or not reachable; container not running |
| Chat replies but no image shows | `App__PublicBaseUrl` wrong or not publicly reachable (LINE can't fetch `/media/{id}`) |
| `400 "Model not supported by provider hf-inference"` on edit/video | `ImageEditEndpoint`/`VideoEndpoint` (or the model id) points at hf-inference; use the fal-ai defaults above |
| Other "error" on edit/video | HF token missing Inference Providers permission, or **out of credits** (fal-ai burns them fast) |
| No rich menu | `App__RichMenuEnabled=false`, or the channel access token lacks rich-menu scope |

To see the actual error, check the container logs: `docker logs line-hf-bot` (look for `Failed to handle item ...`).

---

## Appendix — Publish your own image (CI/CD)

Only needed if you build and publish the image yourself (e.g. from a fork). Consumers can skip this.

### Automated (GitHub Actions)

The repo ships two workflows:

- `.github/workflows/ci.yml` — builds and tests (and does a no-push Docker build) on every push/PR to `main`.
- `.github/workflows/release.yml` — builds a **multi-arch** image (`linux/amd64`, `linux/arm64`) and pushes it
  to Docker Hub when you push a **version tag** (`v*`).

**One-time setup**

1. Create a Docker Hub access token (Docker Hub → *Account Settings → Personal access tokens*, scope **Read & Write**).
2. Add two GitHub repo secrets (*Settings → Secrets and variables → Actions*):
   - `DOCKERHUB_USERNAME` — your Docker Hub account/namespace (also the image namespace).
   - `DOCKERHUB_TOKEN` — the access token.

**Release**

```bash
git tag v1.0.0
git push origin v1.0.0
```

Publishes `<DOCKERHUB_USERNAME>/line-hf-bot:1.0.0`, `:1.0`, and `:latest`. Pre-release tags (`v1.0.0-rc1`) are
published but do not move `latest`.

### Manual

```bash
docker build -t <your-user>/line-hf-bot:1.0.0 -t <your-user>/line-hf-bot:latest .
docker login
docker push <your-user>/line-hf-bot:1.0.0
docker push <your-user>/line-hf-bot:latest
```

> Behind a corporate TLS-inspecting proxy? Drop your root CA `*.crt` files into `certs/` before building; the
> Dockerfile trusts them (they are gitignored and never published).
