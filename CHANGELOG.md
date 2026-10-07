# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Each release is published to Docker Hub as
[`pierre3/line-hf-bot`](https://hub.docker.com/r/pierre3/line-hf-bot) (multi-arch: `linux/amd64`, `linux/arm64`).

## [1.3.1] - 2026-10-07

### Security
- Rebuilt on the latest `aspnet:10.0-noble-chiseled-extra` base image, picking up Ubuntu fixes for openssl
  (1 High, 2 Medium, 8 Low) and glibc (6 Medium). Docker Scout findings drop from 20 to 3; the remaining
  3 (glibc ×2, icu ×1) have no upstream fix yet.
- The release workflow now always pulls the newest base image (`pull: true`) instead of relying on the build cache.

## [1.3.0] - 2026-08-21

### Added
- **Deploy to Azure button** for one-click deployment to Azure Container Apps — no CLI needed. Enter your
  LINE and Hugging Face credentials in the portal form; the webhook URL is shown when the deploy finishes.
  The form also lets you choose the language, vision/video options, container size, chat model
  (`chatModel`), and minimum replicas (`minReplicas`: `1` = always-on, recommended; `0` = scale to zero, cheaper
  but loses in-memory state and cold-starts after idle). See [Azure Container Apps](docs/deploy/azure-container-apps.md).
- **Chat troubleshooting** section in the README (how to find a currently served chat model via `GET /v1/models`).

### Changed
- Default chat model `HuggingFace__ChatModel` is now `Qwen/Qwen2.5-72B-Instruct` (was `Qwen/Qwen2.5-7B-Instruct`,
  which providers stopped serving and which caused chat to fail with `model_not_supported`).

### Fixed
- The Azure deploy form no longer allows invalid CPU/memory combinations: a single "Container size" dropdown
  lists only the pairs Container Apps accepts.

## [1.2.0] - 2026-08-18

### Added
- **Conversational vision follow-up on images** (spec09). After the bot answers a question about an image,
  plain messages continue as follow-up questions about the **same** image, with the prior Q&A resent for
  context (so pronouns like "what color is it?" resolve). Tap **💬 Chat** (or switch mode / send a slash
  command / send a new image) to leave the session.
- **💬 Ask button on generated and edited image results** (shown when `App__VisionEnabled=true`), so you can
  ask about an image the bot just made — not only about photos you send.
- **`App__VisionMaxTurns`** setting (default `8`, minimum 1) caps how many Q&A turns a vision session keeps.
  Each follow-up resends the image plus prior turns, so this bounds credit usage.

## [1.1.1] - 2026-08-18

### Fixed
- Videos now play **inline** in the LINE app instead of showing a black frame. The `/media/{id}` endpoint now
  serves HTTP range requests (`enableRangeProcessing`), which LINE's inline player requires.

## [1.1.0] - 2026-08-17

### Added
- **Image Q&A (vision/VQA)** on user-sent photos (spec07): send a photo and ask a one-shot question, answered
  by a vision model over the same Hugging Face Inference credits as chat. On by default (`App__VisionEnabled`).
- **Image-to-video** — turn a working image into a short clip via **🎬 Make a video** (spec08), on the fal-ai
  provider. Gated by `App__VideoEnabled` (shared with `/video`); off by default.

## [1.0.1] - 2026-08-17

### Changed
- Runtime image switched to an Ubuntu **chiseled** (distroless-style) base — no shell or package manager,
  non-root by default, and a much smaller CVE surface.

### Fixed
- Updated CI/CD GitHub Actions to current major versions (removed the Node 20 deprecation warnings).
- Added a Docker Hub Overview (repository description).

## [1.0.0] - 2026-08-16

### Added
- First public release. A LINE bot that talks to Hugging Face models:
  - 💬 **Chat** with per-user conversation history.
  - 🎨 **Image generation** (`/image` or Image mode), with a provider-agnostic response path (raw bytes or a
    JSON URL re-fetched behind an SSRF allowlist).
  - 🖼️ **Image editing** (image-to-image) of a generated image or a photo you send, via the fal-ai provider.
  - 🎬 **Text-to-video** (`/video`) via the fal-ai provider — off by default (`App__VideoEnabled`).
  - 🎛️ A **rich menu** to switch Chat / Image / Video modes, with 🔄 Regenerate / ✏️ Edit quick replies on results.
  - 🌐 **English / Japanese** UI (`App__Locale`).
  - 🐳 Published as a multi-arch Docker image with CI/CD release automation.

[1.3.1]: https://github.com/pierre3/line-hf-bot/releases/tag/v1.3.1
[1.3.0]: https://github.com/pierre3/line-hf-bot/releases/tag/v1.3.0
[1.2.0]: https://github.com/pierre3/line-hf-bot/releases/tag/v1.2.0
[1.1.1]: https://github.com/pierre3/line-hf-bot/releases/tag/v1.1.1
[1.1.0]: https://github.com/pierre3/line-hf-bot/releases/tag/v1.1.0
[1.0.1]: https://github.com/pierre3/line-hf-bot/releases/tag/v1.0.1
[1.0.0]: https://github.com/pierre3/line-hf-bot/releases/tag/v1.0.0
