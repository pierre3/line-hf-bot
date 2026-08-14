# line-hf-bot

English | [日本語](README.ja.md)

A LINE bot that uses Hugging Face models for **AI chat, image generation, and video generation**.
Built on ASP.NET (.NET 10). The aim is to keep it easy to run: start the Docker image on your PC,
expose it through a tunnel, and connect it to LINE. It can also be hosted in the cloud.

> ⚠️ **Work in progress** — currently at the spec / early-implementation stage.

## Features (planned)
- 💬 Text chat with conversation history (Semantic Kernel's Hugging Face connector)
- 🎨 Image generation (`/image <prompt>`)
- 🎬 Video generation (`/video <prompt>`)
- 🐳 Shipped as a Docker image. Run locally with a dev tunnel, or host it in the cloud.

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
