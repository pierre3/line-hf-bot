# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Each release is published to Docker Hub as
[`pierre3/line-hf-bot`](https://hub.docker.com/r/pierre3/line-hf-bot) (multi-arch: `linux/amd64`, `linux/arm64`).

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

[1.2.0]: https://github.com/pierre3/line-hf-bot/releases/tag/v1.2.0
[1.1.1]: https://github.com/pierre3/line-hf-bot/releases/tag/v1.1.1
[1.1.0]: https://github.com/pierre3/line-hf-bot/releases/tag/v1.1.0
[1.0.1]: https://github.com/pierre3/line-hf-bot/releases/tag/v1.0.1
[1.0.0]: https://github.com/pierre3/line-hf-bot/releases/tag/v1.0.0
