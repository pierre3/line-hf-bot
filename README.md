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
- 🎬 **Video generation** — `/video <prompt>` (text-to-video) and **🎬 Make a video** from an image (image-to-video), both via the **fal-ai** provider. **Off by default** (`App:VideoEnabled` gates both) because fal-ai burns through Hugging Face credits fast and is slow; set it to `true` to enable
- 🎞️ **Animate an image** (image-to-video) — when video is enabled, image results and sent photos offer **🎬 Make a video**; tap it, describe the motion (e.g. "slowly zoom in"), and the image is turned into a short clip via fal-ai
- 🎛️ **Mode rich menu** — a bottom menu switches between Chat / Image / Video; a plain message is
  interpreted by the current mode, so no prefix is needed. Image results offer 🔄 Regenerate, ✏️ Edit (image-to-image), 💬 Ask about this image (when vision enabled), 🎬 Make a video (when enabled), and 💬 Chat.
- 🖼️ **Send a photo** — the bot offers **✏️ Edit** (image-to-image), **💬 Ask about this image** (vision Q&A), and **🎬 Make a video** (image-to-video, when enabled). Tap one, then send your instruction, question, or motion. (With `App:VisionEnabled=false` a photo goes straight to editing.)
- 🔍 **Ask about an image** (vision/VQA) — ask about a photo you sent or an image the bot made, answered by a vision model over the same HF Inference credits as chat (not the credit-heavy fal provider). **Follow-up questions continue in context** — just keep typing; tap 💬 Chat to leave. On by default; needs a vision model your token can serve (`HuggingFace:VisionModel`). Each follow-up resends the image + prior turns, so cost grows with turns (capped by `App:VisionMaxTurns`)
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
- **Editing/video burn credits fast.** All generation draws down your Hugging Face Inference Providers credits
  (there's a free monthly allowance). Image editing and video use the **fal-ai** provider, which costs much
  more per call than hf-inference chat/image — so those eat your credits quickly. Video is off by default.

## Getting started

The quickest path: run the **published Docker Hub image** locally and expose it with a tunnel — no source
checkout or build needed. Other options are under [Other ways to run](#other-ways-to-run).

### Prerequisites
- A **LINE Messaging API channel** — its **Channel secret** and a long-lived **Channel access token**
- A **Hugging Face token** with the **Inference Providers** permission
- **Docker**
- A tunnel for a public HTTPS URL. The steps below use [Dev Tunnels](https://learn.microsoft.com/azure/developer/dev-tunnels/)
  (one-time `devtunnel user login`); ngrok or Cloudflare Tunnel work too.

### 1. Start a tunnel — get your public URL first
Do this first so you know the HTTPS URL before writing `.env`. Keep it running:
```bash
devtunnel host -p 8080 --allow-anonymous
```
Copy the `https://…devtunnels.ms` URL it prints — that's your `App__PublicBaseUrl`.

### 2. Create your `.env`
In a new terminal, paste the tunnel URL into `App__PublicBaseUrl` and fill in your three tokens:
```bash
cat > .env <<'EOF'
Line__ChannelSecret=<your channel secret>
Line__ChannelAccessToken=<your channel access token>
HuggingFace__ApiKey=hf_xxxxxxxxxxxxxxxxx
App__PublicBaseUrl=https://<your-tunnel>.devtunnels.ms
EOF
```
Everything else has a sensible default. Full list: [parameter reference](docs/deploy/docker-hub.md#parameter-reference).

### 3. Pull and run the image
```bash
docker pull pierre3/line-hf-bot:latest
docker run --env-file .env -p 8080:8080 pierre3/line-hf-bot:latest
```
Check it's up (in another terminal):
```bash
curl http://localhost:8080/health      # -> {"status":"ok"}
```

### 4. Point LINE at the webhook
Enable **Use webhook** (and turn off auto-reply) in the LINE console. Set the webhook URL to
`https://<your-tunnel>.devtunnels.ms/webhook` there, or use the `line` CLI
([Line.OpenApi.Tools](https://github.com/pierre3/line-openapi-dotnet), needs the .NET SDK):
```bash
dotnet tool install -g Line.OpenApi.Tools
line config set default --token "YOUR_CHANNEL_ACCESS_TOKEN"
line webhook set-endpoint --url "https://<your-tunnel>.devtunnels.ms/webhook"
line webhook test-endpoint
```

### 5. Chat
Add the bot as a friend (QR code in the LINE console) and message it. Full walkthrough, parameter
reference, and troubleshooting: **[Run from Docker Hub](docs/deploy/docker-hub.md)**.

### Other ways to run
- **[Azure Container Apps](docs/deploy/azure-container-apps.md)** — host on a managed HTTPS endpoint (no tunnel needed). One click, no CLI:

  [![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fpierre3%2Fline-hf-bot%2Fmain%2Finfra%2Fazuredeploy.json/createUIDefinitionUri/https%3A%2F%2Fraw.githubusercontent.com%2Fpierre3%2Fline-hf-bot%2Fmain%2Finfra%2FcreateUiDefinition.json)

  Fill in your three credentials in the browser; the app's webhook URL is shown when the deploy finishes — paste it into LINE. It runs as a single **always-on** replica (required by the in-memory design), so expect a small ongoing cost.
- **[Run from source](docs/deploy/from-source.md)** — build from a clone with Docker Compose or `dotnet run`, for development or customization.

## Commands
| Input | Result |
| --- | --- |
| any text | interpreted by the current mode (chat / image / video) |
| a photo | the bot offers ✏️ Edit / 💬 Ask about this image / 🎬 Make a video (when video enabled); tap one, then your next message is applied (`App:VisionEnabled=false` → straight to editing) |
| a follow-up while asking | after a vision answer, plain messages keep asking about the same image in context; tap 💬 Chat (or switch mode) to leave |
| `/image <prompt>` | generate an image |
| `/video <prompt>` | generate a video (text-to-video via fal-ai); off by default, see `App:VideoEnabled` |
| 🎬 Make a video | turn the working image into a short clip (image-to-video via fal-ai); shown on image results and sent photos when `App:VideoEnabled=true` |
| `/reset` | clear conversation history and reset mode |
| `/help` | show usage |

Slash commands work in any mode without changing it. Image results carry 🔄 Regenerate / ✏️ Edit / 💬 Ask about this (when vision enabled) / 🎬 Make a video (when video enabled) / 💬 Chat buttons.

## Configuration
All settings are environment variables (`Section__Key`). See [`.env.example`](.env.example) for the full list; the essentials:

| Variable | Notes |
| --- | --- |
| `Line__ChannelSecret` / `Line__ChannelAccessToken` | LINE channel credentials (required) |
| `Line__MaxIncomingImageBytes` / `Line__ContentFetchTimeoutSeconds` | limits for downloading a user-sent image to edit (default 10 MB / 30 s) |
| `HuggingFace__ApiKey` | HF token with Inference Providers permission (required) |
| `HuggingFace__ChatModel` | chat model, default `Qwen/Qwen2.5-72B-Instruct` (non-gated). Availability depends on your enabled Inference Providers — see [Chat troubleshooting](#chat-troubleshooting) if replies fail |
| `HuggingFace__ImageEditModel` / `HuggingFace__ImageEditEndpoint` | image-to-image via the **fal-ai** provider (default `fal-ai/qwen-image-edit`). hf-inference doesn't serve image-to-image; fal-ai costs more credits per call than hf-inference |
| `HuggingFace__VideoModel` / `HuggingFace__VideoEndpoint` | text-to-video via the **fal-ai** provider (default `fal-ai/wan/v2.2-5b/text-to-video`). hf-inference doesn't serve text-to-video; fal-ai is credit-heavy and slow |
| `HuggingFace__ImageToVideoModel` / `HuggingFace__ImageToVideoEndpoint` | image-to-video via the **fal-ai** provider (default `fal-ai/wan/v2.2-a14b/image-to-video`; lighter alternative `fal-ai/wan-i2v`). hf-inference doesn't serve image-to-video; A14B costs more credits than the 5B text-to-video default. Gated by `App__VideoEnabled` |
| `HuggingFace__VisionModel` / `HuggingFace__VisionEndpoint` | vision Q&A for sent photos, via a vision chat model on the OpenAI-compatible endpoint. Uses chat-level HF credits (not fal). Pin the provider (`model:provider`) and enable it in HF settings — default `Qwen/Qwen2.5-VL-72B-Instruct:ovhcloud` needs **ovhcloud** enabled. See [vision troubleshooting](#vision-troubleshooting) |
| `HuggingFace__MediaRefetchAllowedHosts` | hosts allowed when re-fetching media from a provider URL (default `fal.media;replicate.delivery`; empty = deny all) |
| `App__PublicBaseUrl` | your tunnel's HTTPS base (required for images) |
| `App__Locale` | UI language for user-facing text and the rich menu (`en` default, or `ja`) |
| `App__RichMenuEnabled` | provision the mode rich menu on startup (default `true`) |
| `App__VideoEnabled` | enable video: `/video` (text-to-video) **and** 🎬 Make a video (image-to-video). Both run on the credit-heavy, slow fal-ai provider; default `false` |
| `App__VisionEnabled` | vision Q&A on sent photos and generated images (default `true`). On: a sent photo offers Edit/Ask and image results add a 💬 Ask button; off: a sent photo goes straight to editing (no vision UI) |
| `App__VisionMaxTurns` | max Q&A turns kept in a conversational vision session (default `8`, min 1). Each follow-up resends the image + prior turns, so credit cost grows with turns — this caps it |

### Vision troubleshooting
The "Ask about this image" answer is generated by a vision model on Hugging Face Inference Providers, so the reply depends on that provider serving the model for your token. If asking fails:
- **`model_not_supported`** — auto-routing didn't pick a provider. Always pin the provider in `HuggingFace__VisionModel` as `model:provider` (e.g. `Qwen/Qwen2.5-VL-72B-Instruct:ovhcloud`) and enable that provider at https://huggingface.co/settings/inference-providers.
- **`capacity_exhausted` (503) or timeout** — the provider is busy or cold. Retry, or switch to another provider/model. Working alternatives: `zai-org/GLM-4.5V:novita`, `google/gemma-3-27b-it:deepinfra` (gemma requires accepting its license), `Qwen/Qwen2.5-VL-7B-Instruct:featherless-ai`. Enable the target provider first.
- A cold first request can be slow; `HuggingFace__VisionTimeoutSeconds` (default 120) bounds it.

### Chat troubleshooting
Chat runs through Hugging Face Inference Providers, so `HuggingFace__ChatModel` must be served by a provider your token has enabled. If the bot replies **"Something went wrong"** for plain messages, check the container logs — a `model_not_supported` / `400` from the router is the usual cause:
- **Provider catalogs change over time**, so a model that worked before can stop being served even with no change on your side. List what's available now and switch to one:
  ```
  curl https://router.huggingface.co/v1/models -H "Authorization: Bearer <HF token>"
  ```
  Then set `HuggingFace__ChatModel` to a served chat model (pin a provider as `model:provider` if needed) and enable providers at https://huggingface.co/settings/inference-providers.
- Lighter/cheaper alternatives to the 72B default: `meta-llama/Llama-3.1-8B-Instruct`, `Qwen/Qwen3-4B-Instruct-2507`. Avoid `*-Coder` (code-only) and `*-VL` (vision) models for general chat.

## Tech stack
- .NET 10 / ASP.NET Minimal API
- [pierre3/line-openapi-dotnet](https://github.com/pierre3/line-openapi-dotnet) (`Line.OpenApi.Bot`)
- [Semantic Kernel](https://github.com/microsoft/semantic-kernel) (Hugging Face connector)
- Hugging Face Inference Providers (image / video / vision)

## Documentation
- Changelog: [`CHANGELOG.md`](CHANGELOG.md)
- Deployment guides: [`docs/deploy/`](docs/deploy/) — [Run from Docker Hub & LINE setup](docs/deploy/docker-hub.md), [Azure Container Apps](docs/deploy/azure-container-apps.md), [Run from source](docs/deploy/from-source.md)
- Specs: [`docs/specs/`](docs/specs/) — 01 base bot, 02 image provider, 03 mode / rich menu / i18n, 04 editing user-sent photos, 05 image editing via fal-ai, 06 video via fal-ai, 07 image Q&A (vision/VQA), 08 image-to-video, 09 vision follow-up / multi-turn
- Review records (spec / implementation / security / documentation gates): [`docs/reviews/`](docs/reviews/)
- Developer guide (architecture notes): [`CLAUDE.md`](CLAUDE.md)

## License
[MIT](LICENSE).
