# line-hf-bot

English | [日本語](README.ja.md)

A LINE bot that uses Hugging Face models for **AI chat and image generation** (with video planned).
Built on ASP.NET (.NET 10). The aim is to keep it easy to run: start the Docker image on your PC,
expose it through a tunnel, and connect it to LINE. It can also be hosted in the cloud.

## Features
- 💬 **Chat** with conversation history (Semantic Kernel + Hugging Face)
- 🎨 **Image generation** — `/image <prompt>` (served back to LINE from the app)
- 🎬 Video generation — scaffolded but **off by default** (`App:VideoEnabled`); needs a video provider integration
- 🐳 Ships as a Docker image; run locally with a tunnel, or host in the cloud

## How it works
```
LINE → POST /webhook (verify signature, return 200 immediately)
     → in-memory queue → background workers → Hugging Face
     → reply/push back to LINE  (images are hosted at /media/{id})
```
LINE requires a public HTTPS URL for images, so the app hosts generated media itself and hands LINE the URL.

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
| any text | AI chat reply |
| `/image <prompt>` | generate an image |
| `/video <prompt>` | disabled by default (see `App:VideoEnabled`) |
| `/reset` | clear conversation history |
| `/help` | show usage |

## Configuration
All settings are environment variables (`Section__Key`). See [`.env.example`](.env.example) for the full list; the essentials:

| Variable | Notes |
| --- | --- |
| `Line__ChannelSecret` / `Line__ChannelAccessToken` | LINE channel credentials (required) |
| `HuggingFace__ApiKey` | HF token with Inference Providers permission (required) |
| `HuggingFace__ChatModel` | default `Qwen/Qwen2.5-7B-Instruct` (non-gated) |
| `App__PublicBaseUrl` | your tunnel's HTTPS base (required for images) |

## Publish to Docker Hub
```bash
docker build -t <your-user>/line-hf-bot .
docker push <your-user>/line-hf-bot
# run anywhere:
docker run --env-file .env -p 8080:8080 <your-user>/line-hf-bot
```

## Tech stack
- .NET 10 / ASP.NET Minimal API
- [pierre3/line-openapi-dotnet](https://github.com/pierre3/line-openapi-dotnet) (`Line.OpenApi.Bot`)
- [Semantic Kernel](https://github.com/microsoft/semantic-kernel) (Hugging Face connector)
- Hugging Face Inference Providers (image / video)

## Documentation
- Spec: [`docs/specs/01-line-hf-bot.md`](docs/specs/01-line-hf-bot.md)
- Review records: [`docs/reviews/`](docs/reviews/)
- Developer guide: [`CLAUDE.md`](CLAUDE.md)

## License
Not decided yet.
