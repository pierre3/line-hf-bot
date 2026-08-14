#requires -Version 7
<#
.SYNOPSIS
    Build and run the containerized bot, then health-check it.
    Handles the corporate TLS-inspecting proxy case by trusting the host's root CAs.

.PARAMETER Rebuild
    Rebuild the image (docker compose up --build). Use after code or cert changes.

.PARAMETER ExportCerts
    Force re-exporting the host root CAs into ./certs (done automatically if ./certs has none).

.EXAMPLE
    ./scripts/run.ps1
.EXAMPLE
    ./scripts/run.ps1 -Rebuild
#>
[CmdletBinding()]
param(
    [switch]$Rebuild,
    [switch]$ExportCerts
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# 1. Trust host root CAs (needed behind a corporate TLS-inspecting proxy so the container
#    can restore packages and reach Hugging Face / LINE over HTTPS). No-op on normal networks.
$certDir = Join-Path $root 'certs'
$haveCerts = @(Get-ChildItem (Join-Path $certDir '*.crt') -ErrorAction SilentlyContinue).Count -gt 0
if ($ExportCerts -or -not $haveCerts) {
    Write-Host 'Exporting host root CAs into ./certs ...'
    Get-ChildItem Cert:\LocalMachine\Root | ForEach-Object {
        $pem = "-----BEGIN CERTIFICATE-----`n" +
               [Convert]::ToBase64String($_.RawData, 'InsertLineBreaks') +
               "`n-----END CERTIFICATE-----"
        Set-Content -Path (Join-Path $certDir "root_$($_.Thumbprint).crt") -Value $pem -Encoding ascii
    }
}

# 2. Ensure .env exists (from the template) and is filled in.
if (-not (Test-Path '.env')) {
    Copy-Item '.env.example' '.env'
    Write-Warning 'Created .env from .env.example. Fill in Line__ChannelSecret, Line__ChannelAccessToken, HuggingFace__ApiKey and (for images) App__PublicBaseUrl, then re-run this script.'
    return
}

# 3. Build & run.
if ($Rebuild) { docker compose up --build -d } else { docker compose up -d }
if ($LASTEXITCODE -ne 0) { throw 'docker compose failed. Is Docker running? Is HOST_PORT free?' }

# 4. Health check on the configured host port.
$portMatch = Select-String -Path '.env' -Pattern '^HOST_PORT=(\d+)' -ErrorAction SilentlyContinue
$port = if ($portMatch) { $portMatch.Matches[0].Groups[1].Value } else { '8080' }
$healthUrl = "http://localhost:$port/health"
Write-Host "Waiting for $healthUrl ..."
$ok = $false
for ($i = 0; $i -lt 30; $i++) {
    try {
        if ((Invoke-WebRequest $healthUrl -TimeoutSec 3 -SkipHttpErrorCheck).StatusCode -eq 200) { $ok = $true; break }
    } catch { }
    Start-Sleep -Seconds 2
}
if ($ok) { Write-Host "Health OK: $healthUrl" -ForegroundColor Green }
else { Write-Warning "Health check did not pass. Inspect logs: docker compose logs -f" }

# 5. Next steps (the tunnel is interactive, so it stays manual).
Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Cyan
Write-Host "  1) Expose the port:  devtunnel host -p $port --allow-anonymous"
Write-Host '  2) Set the webhook:  ./scripts/set-webhook.ps1 -TunnelUrl https://<your-tunnel>'
Write-Host '  3) Message your bot on LINE (chat, /image ...)'
