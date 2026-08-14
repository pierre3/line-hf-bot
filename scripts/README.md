# scripts/

`run.ps1` (PowerShell 7+) automates the containerized setup and verification in one command.
It trusts the host root CAs (for a corporate proxy), creates `.env` if missing, builds/runs the
container, health-checks it, and — when a tunnel URL is given — sets and verifies the LINE webhook.

## English

Prerequisites: Docker, PowerShell 7+, a tunnel tool (e.g. `devtunnel`), a filled-in `.env`.

Start the tunnel first (its URL is known immediately), then run everything in one command:
```powershell
devtunnel host -p 8081 --allow-anonymous
./scripts/run.ps1 -Port 8081 -TunnelUrl https://<your-tunnel> -Rebuild
```
Then message your bot on LINE (chat, `/image ...`).

Just build/run/health-check (set the webhook later): `./scripts/run.ps1`
Parameters: `-Port <n>` (host port), `-TunnelUrl <https://...>`, `-Rebuild`, `-ExportCerts`.

Logs: `docker compose logs -f` — Stop: `docker compose down`

## 日本語

前提: Docker、PowerShell 7+、トンネルツール（例: `devtunnel`）、記入済みの `.env`。

トンネルを先に起動（URL はすぐ分かる）してから、1コマンドで一括実行:
```powershell
devtunnel host -p 8081 --allow-anonymous
./scripts/run.ps1 -Port 8081 -TunnelUrl https://<トンネル> -Rebuild
```
あとは LINE からボットに送信（チャット・`/image ...`）。

起動＆ヘルスチェックのみ（Webhook は後で設定）: `./scripts/run.ps1`
引数: `-Port <番号>`（ホストポート）, `-TunnelUrl <https://...>`, `-Rebuild`, `-ExportCerts`。

ログ: `docker compose logs -f` ／ 停止: `docker compose down`
