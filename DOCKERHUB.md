# line-hf-bot

A LINE bot that uses **Hugging Face** models for **AI chat, image generation, image editing, and video
generation** — built on ASP.NET (.NET 10). Run this image on your PC (exposed through a tunnel) or on a cloud
host, connect it to a LINE Messaging API channel, and chat with Hugging Face models straight from LINE.

- 📦 **Source & full docs:** https://github.com/pierre3/line-hf-bot
- 🐛 **Issues:** https://github.com/pierre3/line-hf-bot/issues

## Supported tags

- `latest` — the most recent release
- `1.2.3`, `1.2` — pinned semantic-version tags

Multi-arch: `linux/amd64` and `linux/arm64`. The runtime is an Ubuntu **chiseled** (distroless-style) image —
no shell or package manager, non-root by default, minimal CVE surface.

## Quick start

Create a `.env` file (fill in your own values):

```dotenv
Line__ChannelSecret=<your channel secret>
Line__ChannelAccessToken=<your channel access token>
HuggingFace__ApiKey=hf_xxxxxxxxxxxxxxxxx
App__PublicBaseUrl=https://<your-public-host>
```

`App__PublicBaseUrl` is the public HTTPS URL where LINE can reach this app (a tunnel URL when running locally,
or the cloud host's HTTPS endpoint). Then:

```bash
docker run --env-file .env -p 8080:8080 pierre3/line-hf-bot:latest
curl http://localhost:8080/health      # -> {"status":"ok"}
```

Point your LINE channel's webhook at `https://<your-public-host>/webhook` and start chatting.

Full walkthrough (tunnel, `.env` parameter reference, LINE setup, troubleshooting):
**https://github.com/pierre3/line-hf-bot/blob/main/docs/deploy/docker-hub.md**
· Azure Container Apps: **https://github.com/pierre3/line-hf-bot/blob/main/docs/deploy/azure-container-apps.md**

## What you can do

- 💬 **Chat** with conversation history
- 🎨 **Image generation** — `/image <prompt>` or switch to Image mode
- 🖼️ **Image editing** — edit a generated image or a photo you send (image-to-image, via fal-ai)
- 🎬 **Video generation** — `/video <prompt>` (text-to-video, via fal-ai; off by default)
- 🎛️ A **rich menu** switches modes; **English / Japanese** UI (`App__Locale`)

## Configuration

Everything is set through environment variables (`Section__Key`, double underscore). The four above are the
minimum; models, providers, timeouts, and behavior all have sensible defaults. Full reference:
**https://github.com/pierre3/line-hf-bot/blob/main/docs/deploy/docker-hub.md#parameter-reference**

## Important: state is in memory

Conversation history and generated media live **only in process memory** (with a TTL cache) and are **lost on
restart**. There is no database. Run **exactly one instance** — this image is not designed for horizontal
scaling. See the project's Limitations section for details.

## License

[MIT](https://github.com/pierre3/line-hf-bot/blob/main/LICENSE) · © 2026 Hirotada Kobayashi
