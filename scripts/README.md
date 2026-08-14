# scripts/

Helper scripts (PowerShell 7+) that automate the containerized setup and verification.

## English

Prerequisites: Docker, PowerShell 7+, a tunnel tool (e.g. `devtunnel`), a filled-in `.env`.

1. **Build, run, health-check** (also trusts host root CAs for a corporate proxy, creates `.env` if missing):
   ```powershell
   ./scripts/run.ps1            # or -Rebuild after code/cert changes
   ```
2. **Expose the port** (interactive — leave it running):
   ```powershell
   devtunnel host -p 8081 --allow-anonymous     # use your HOST_PORT
   ```
3. **Set & verify the webhook** (updates `App__PublicBaseUrl`, recreates the container, runs the `line` CLI):
   ```powershell
   ./scripts/set-webhook.ps1 -TunnelUrl https://<your-tunnel>
   ```
4. Message your bot on LINE (chat, `/image ...`).

Logs: `docker compose logs -f` — Stop: `docker compose down`

## 日本語

前提: Docker、PowerShell 7+、トンネルツール（例: `devtunnel`）、記入済みの `.env`。

1. **ビルド・起動・ヘルスチェック**（企業プロキシ用に host のルート CA も信頼、`.env` が無ければ作成）:
   ```powershell
   ./scripts/run.ps1            # コードや cert 変更後は -Rebuild
   ```
2. **ポートを公開**（対話的。起動したままにする）:
   ```powershell
   devtunnel host -p 8081 --allow-anonymous     # HOST_PORT に合わせる
   ```
3. **Webhook を設定・検証**（`App__PublicBaseUrl` を更新→コンテナ再作成→`line` CLI 実行）:
   ```powershell
   ./scripts/set-webhook.ps1 -TunnelUrl https://<トンネル>
   ```
4. LINE からボットに送信（チャット・`/image ...`）。

ログ: `docker compose logs -f` ／ 停止: `docker compose down`
