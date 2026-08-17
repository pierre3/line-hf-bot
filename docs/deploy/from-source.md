# Deploy: Run from source (build from a clone)

English | [日本語](from-source.ja.md)

Build and run the bot from a **local clone** of the repository — with Docker Compose or the .NET SDK. This is
for **development or customization**. If you just want to run the bot, use the published image instead:
[Run from Docker Hub](docker-hub.md).

Related: [Run from Docker Hub](docker-hub.md) · [Azure Container Apps](azure-container-apps.md).

---

## Prerequisites

- The repository **cloned** locally.
- **Docker** (for the Compose path) *or* the **.NET 10 SDK** (for the `dotnet run` path).
- The same credentials and public URL as the Docker Hub guide: a **LINE Messaging API channel** (Channel
  secret + access token), a **Hugging Face token** with the **Inference Providers** permission, and a
  **public HTTPS tunnel** (Dev Tunnels / ngrok / Cloudflare Tunnel).

---

## 1. Start a tunnel — get your public URL first

Do this first so you know the HTTPS URL before writing `.env`. Keep it running:

```bash
devtunnel host -p 8080 --allow-anonymous
```

Copy the `https://…devtunnels.ms` URL it prints — that's your `App__PublicBaseUrl`.

> If 8080 is already in use, pick another port (e.g. `-p 8081`), set `HOST_PORT=8081` in `.env` (Compose path),
> and use that same port everywhere below.

---

## 2. Configure

The repo ships [`.env.example`](../../.env.example) with **every** setting documented. Copy it and fill in your values:

```bash
cp .env.example .env
# edit .env: Line__ChannelSecret, Line__ChannelAccessToken, HuggingFace__ApiKey, App__PublicBaseUrl
```

Set `App__PublicBaseUrl` to the tunnel URL from step 1. See the Docker Hub guide's
[parameter reference](docker-hub.md#parameter-reference) for what every key does.

---

## 3a. Run with Docker Compose (builds the image locally)

The repo's [`compose.yaml`](../../compose.yaml) builds the image from the `Dockerfile` and reads `.env`:

```bash
docker compose up --build      # listens on :8080
```

To expose it on a different host port, set `HOST_PORT` in `.env` (e.g. `HOST_PORT=8081`) — the container
always listens on 8080 internally.

## 3b. Or run with the .NET SDK

For an SDK-only dev loop (no Docker), use `dotnet user-secrets` for the tokens instead of `.env`:

```bash
cd LineHfBot
dotnet user-secrets init
dotnet user-secrets set "Line__ChannelSecret" "<your channel secret>"
dotnet user-secrets set "Line__ChannelAccessToken" "<your channel access token>"
dotnet user-secrets set "HuggingFace__ApiKey" "hf_xxxxxxxxxxxxxxxxx"
dotnet user-secrets set "App__PublicBaseUrl" "https://<your-tunnel>.devtunnels.ms"
cd ..
dotnet run --project LineHfBot --urls http://localhost:8080
```

The `--urls` keeps the port at 8080 to match the tunnel.

> **Windows one-liner.** `scripts/run.ps1 -Port 8081 -StartTunnel -Rebuild` builds the image, exports host
> root CA certs into `certs/` (see below), starts a Dev Tunnel, and runs the container in one go.

### Verify it started

```bash
curl http://localhost:8080/health      # -> {"status":"ok"}
```

---

## 4. Point LINE at the webhook, then chat

These steps are identical to the Docker Hub guide — follow
[Point LINE at the webhook](docker-hub.md#4-point-line-at-the-webhook) and
[Verify in the app](docker-hub.md#5-verify-in-the-app).

---

## Corporate TLS-inspecting proxy

Behind a proxy that intercepts TLS, the build/runtime needs your organization's root CA. Drop the root CA
`*.crt` files into `certs/` before building; the `Dockerfile` trusts them. They are gitignored and never
published. (`scripts/run.ps1` exports the host's root CAs into `certs/` automatically.)

---

## Publishing your own image

To build and push your own image to a registry (e.g. from a fork), see the Docker Hub guide's
[Publish your own image (CI/CD)](docker-hub.md#appendix--publish-your-own-image-cicd) appendix.
