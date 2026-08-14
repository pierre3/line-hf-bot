# scripts/

`run.ps1` (PowerShell 7+) automates the containerized setup and verification in one command:
trust the host root CAs (corporate proxy), create `.env` if missing, build/run the container,
health-check it, host a persistent Dev Tunnel in the background, and set + verify the LINE webhook.

Dev Tunnels (`devtunnel`) is the supported tunnel tool.

## English

Prerequisites: Docker, PowerShell 7+, `devtunnel` (run `devtunnel user login` once), a filled-in `.env`.

One command does everything (tunnel hosted in the background — no extra terminal):
```powershell
./scripts/run.ps1 -Port 8081 -StartTunnel -Rebuild
```
Then message your bot on LINE (chat, `/image ...`). The tunnel is a persistent tunnel named
`line-hf-bot` with a stable URL, so the webhook stays valid across restarts.

Options:
- `-Port <n>` host port (also the tunnel port)
- `-StartTunnel` create/host the persistent tunnel in the background and wire the webhook
- `-TunnelName <id>` persistent tunnel id (default `line-hf-bot`)
- `-TunnelUrl <https://...>` use an explicit URL instead of `-StartTunnel`
- `-Rebuild`, `-ExportCerts`

Just build/run/health-check: `./scripts/run.ps1`

Logs: `docker compose logs -f` — Stop app: `docker compose down` — Stop tunnel: `Get-Process devtunnel | Stop-Process`

## 日本語

前提: Docker、PowerShell 7+、`devtunnel`（初回のみ `devtunnel user login`）、記入済みの `.env`。

1コマンドで全部（トンネルはバックグラウンドで常駐。別ターミナル不要）:
```powershell
./scripts/run.ps1 -Port 8081 -StartTunnel -Rebuild
```
あとは LINE から送信（チャット・`/image ...`）。トンネルは `line-hf-bot` という**永続トンネル**で URL が固定されるため、再起動しても Webhook はそのまま有効です。

オプション:
- `-Port <番号>` ホストポート（トンネルのポートも兼ねる）
- `-StartTunnel` 永続トンネルを作成・バックグラウンド host して Webhook まで設定
- `-TunnelName <id>` 永続トンネル名（既定 `line-hf-bot`）
- `-TunnelUrl <https://...>` `-StartTunnel` の代わりに URL を明示指定
- `-Rebuild`, `-ExportCerts`

起動＆ヘルスチェックのみ: `./scripts/run.ps1`

ログ: `docker compose logs -f` ／ 停止(アプリ): `docker compose down` ／ 停止(トンネル): `Get-Process devtunnel | Stop-Process`
