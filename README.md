# line-hf-bot

English | [日本語](README.ja.md)

A LINE bot that uses Hugging Face models for **AI chat, image generation, image editing, and video generation**.
Built on ASP.NET (.NET 10).

## Purpose
Try Hugging Face models casually through the **LINE chat UI** — no separate app or web console. The goal is
to keep it easy to run: start the Docker image on your PC, expose it through a tunnel, and connect it to LINE
(it can also be hosted in the cloud). Intended for **evaluation and personal use**, not as a multi-user service.

## Features
- 💬 **Chat** with conversation history (Semantic Kernel + Hugging Face)
- 🎨 **Image generation** — `/image <prompt>`, or switch to Image mode and just send a description
- 🎬 **Video generation** — `/video <prompt>` (text-to-video) via the **fal-ai** provider. **Off by default** (`App:VideoEnabled`) because fal is **paid** and slow; set it to `true` to enable
- 🎛️ **Mode rich menu** — a bottom menu switches between Chat / Image / Video; a plain message is
  interpreted by the current mode, so no prefix is needed. Image results offer 🔄 Regenerate, ✏️ Edit (image-to-image), and 💬 Chat.
- 🖼️ **Edit your own photo** — send a photo and the bot asks how to edit it; your next message is applied via image-to-image
- 🌐 **English by default, Japanese available** (`App:Locale` = `en`/`ja`); user-facing text and the rich menu follow it
- 🐳 Ships as a Docker image; run locally with a tunnel, or host in the cloud

Slash commands (`/image`, `/video`, `/reset`, `/help`) always work regardless of mode. The rich menu is
provisioned automatically on startup; set `App:RichMenuEnabled=false` to run without it.

## How it works
```
LINE → POST /webhook (verify signature, return 200 immediately)
     → in-memory queue → background workers → Hugging Face
     → reply/push back to LINE  (images are hosted at /media/{id})
```
LINE requires a public HTTPS URL for images, so the app hosts generated media itself and hands LINE the URL.

## Limitations
Built for easy, small-scale use — mind these trade-offs:

- **Everything is in memory.** Conversation history and generated media (served at `/media/{id}`) live only in
  process memory, with a TTL cache for media. **They are lost on restart or redeploy.** There is no database.
- **Single instance only.** Because state isn't shared, running more than one replica splits history and breaks
  media URLs. It is **not designed for horizontal scaling or redundancy** — run exactly one instance.
- **Paid providers for editing/video.** Image editing and video use the **fal-ai** provider, which is paid and
  needs Hugging Face Inference Providers credits. Video is off by default.

## Getting started

### Prerequisites
- A **LINE Messaging API channel** — get its **Channel secret** and a long-lived **Channel access token**
- A **Hugging Face token** with the **Inference Providers** permission
- A tunnel that gives a public HTTPS URL (e.g. [Dev Tunnels](https://learn.microsoft.com/azure/developer/dev-tunnels/), ngrok, Cloudflare Tunnel)
- Docker (or the .NET 10 SDK for local dev)

### 1. Configure
```bash
cp .env.example .env
# edit .env: Line__ChannelSecret, Line__ChannelAccessToken, HuggingFace__ApiKey, App__PublicBaseUrl
```
`App__PublicBaseUrl` is your tunnel's HTTPS base (used to build image URLs LINE fetches).

### 2. Run
```bash
docker compose up --build      # listens on :8080
```
> If port 8080 is already in use, set `HOST_PORT` (e.g. `HOST_PORT=8081`) in `.env` and use that same port for the tunnel below.

For local development instead: `dotnet run --project LineHfBot --urls http://localhost:8080` and use `dotnet user-secrets` for the values (the `--urls` keeps the port at 8080 to match the tunnel step below).

### 3. Expose with a tunnel
```bash
devtunnel host -p 8080 --allow-anonymous     # note the https URL; also set it as App__PublicBaseUrl
```

### 4. Set the webhook
Enable "Use webhook" (and turn off auto-reply) in the LINE console, then point the webhook at your tunnel.
The `line` CLI ([Line.OpenApi.Tools](https://github.com/pierre3/line-openapi-dotnet), installed as a global tool) makes this easy:
```bash
dotnet tool install -g Line.OpenApi.Tools
line config set default --token "YOUR_CHANNEL_ACCESS_TOKEN"
line webhook set-endpoint --url "https://<tunnel>/webhook"
line webhook test-endpoint
```

### 5. Chat
Add the bot as a friend (QR code in the LINE console) and message it.

## Commands
| Input | Result |
| --- | --- |
| any text | interpreted by the current mode (chat / image / video) |
| a photo | the bot asks how to edit it; your next message edits it (image-to-image) |
| `/image <prompt>` | generate an image |
| `/video <prompt>` | generate a video (text-to-video via fal-ai); off by default, see `App:VideoEnabled` |
| `/reset` | clear conversation history and reset mode |
| `/help` | show usage |

Slash commands work in any mode without changing it. Image results carry 🔄 Regenerate / ✏️ Edit / 💬 Chat buttons.

## Configuration
All settings are environment variables (`Section__Key`). See [`.env.example`](.env.example) for the full list; the essentials:

| Variable | Notes |
| --- | --- |
| `Line__ChannelSecret` / `Line__ChannelAccessToken` | LINE channel credentials (required) |
| `Line__MaxIncomingImageBytes` / `Line__ContentFetchTimeoutSeconds` | limits for downloading a user-sent image to edit (default 10 MB / 30 s) |
| `HuggingFace__ApiKey` | HF token with Inference Providers permission (required) |
| `HuggingFace__ChatModel` | default `Qwen/Qwen2.5-7B-Instruct` (non-gated) |
| `HuggingFace__ImageEditModel` / `HuggingFace__ImageEditEndpoint` | image-to-image via the **fal-ai** provider (default `fal-ai/qwen-image-edit`). hf-inference doesn't serve image-to-image; fal is a **paid** provider (needs Inference Providers credits) |
| `HuggingFace__VideoModel` / `HuggingFace__VideoEndpoint` | text-to-video via the **fal-ai** provider (default `fal-ai/wan/v2.2-5b/text-to-video`). hf-inference doesn't serve text-to-video; fal is **paid** and slow |
| `HuggingFace__MediaRefetchAllowedHosts` | hosts allowed when re-fetching media from a provider URL (default `fal.media;replicate.delivery`; empty = deny all) |
| `App__PublicBaseUrl` | your tunnel's HTTPS base (required for images) |
| `App__Locale` | UI language for user-facing text and the rich menu (`en` default, or `ja`) |
| `App__RichMenuEnabled` | provision the mode rich menu on startup (default `true`) |
| `App__VideoEnabled` | enable `/video` (fal-ai text-to-video is paid; default `false`) |

## Deployment
Two guides cover publishing and hosting the image (each with a Japanese version):

- **[Run from Docker Hub & LINE setup](docs/deploy/docker-hub.md)** — pull the published image, configure
  `.env` (with a full parameter reference), run it, and verify end-to-end in LINE. Publishing/CI-CD is covered
  in an appendix.
- **[Azure Container Apps](docs/deploy/azure-container-apps.md)** — host it on a managed HTTPS endpoint with
  the Azure CLI.

Quick start (pull & run the published image):

```bash
docker pull pierre3/line-hf-bot:latest
docker run --env-file .env -p 8080:8080 pierre3/line-hf-bot:latest
```

CI/CD is wired up too: `.github/workflows/ci.yml` builds and tests every push/PR; `.github/workflows/release.yml`
publishes a multi-arch image to Docker Hub when you push a `v*` tag (see the guide's appendix).

## Tech stack
- .NET 10 / ASP.NET Minimal API
- [pierre3/line-openapi-dotnet](https://github.com/pierre3/line-openapi-dotnet) (`Line.OpenApi.Bot`)
- [Semantic Kernel](https://github.com/microsoft/semantic-kernel) (Hugging Face connector)
- Hugging Face Inference Providers (image / video)

## Documentation
- Deployment guides: [`docs/deploy/`](docs/deploy/) — [Run from Docker Hub & LINE setup](docs/deploy/docker-hub.md), [Azure Container Apps](docs/deploy/azure-container-apps.md)
- Specs: [`docs/specs/`](docs/specs/) — 01 base bot, 02 image provider, 03 mode / rich menu / i18n, 04 editing user-sent photos, 05 image editing via fal-ai, 06 video via fal-ai
- Review records (spec / implementation / security / documentation gates): [`docs/reviews/`](docs/reviews/)
- Developer guide (architecture notes): [`CLAUDE.md`](CLAUDE.md)

## License
[MIT](LICENSE).
