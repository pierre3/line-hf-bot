#requires -Version 7
<#
.SYNOPSIS
    One-shot: build & run the containerized bot, health-check it, and (optionally) point the
    LINE webhook at your tunnel. Handles the corporate TLS-inspecting proxy case by trusting the
    host's root CAs.

.DESCRIPTION
    Start your tunnel first (its URL is known immediately, even before the app is up), then run
    this with -Port and -TunnelUrl to do everything in one go.

.PARAMETER Port
    Host port to expose (written to HOST_PORT in .env). Defaults to the current HOST_PORT, else 8080.

.PARAMETER TunnelUrl
    Public HTTPS base URL of your tunnel. When given, sets App__PublicBaseUrl and configures +
    verifies the LINE webhook via the `line` CLI. Omit to only build/run/health-check.

.PARAMETER Rebuild
    Rebuild the image (docker compose up --build). Use after code or cert changes.

.PARAMETER ExportCerts
    Force re-exporting the host root CAs into ./certs (done automatically if ./certs has none).

.EXAMPLE
    # tunnel first, then one command does the rest:
    devtunnel host -p 8081 --allow-anonymous
    ./scripts/run.ps1 -Port 8081 -TunnelUrl https://abcd-8081.jp.devtunnels.ms -Rebuild

.EXAMPLE
    # just build/run/health-check:
    ./scripts/run.ps1
#>
[CmdletBinding()]
param(
    [int]$Port,
    [string]$TunnelUrl,
    [switch]$Rebuild,
    [switch]$ExportCerts
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Set-EnvKey([string]$Path, [string]$Key, [string]$Value) {
    $lines = @(Get-Content $Path)
    $escaped = [regex]::Escape($Key)
    if ($lines -match "^$escaped=") {
        $lines = $lines -replace "^$escaped=.*", "$Key=$Value"
    } else {
        $lines += "$Key=$Value"
    }
    Set-Content $Path $lines
}

function Get-EnvValue([string]$Path, [string]$Key) {
    $m = Select-String -Path $Path -Pattern "^$([regex]::Escape($Key))=(.*)$"
    if ($m) { return $m.Matches[0].Groups[1].Value } else { return $null }
}

# 1. Trust host root CAs (needed behind a corporate TLS-inspecting proxy so the container can
#    restore packages and reach Hugging Face / LINE over HTTPS). No-op on normal networks.
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
    Write-Warning 'Created .env from .env.example. Fill in Line__ChannelSecret, Line__ChannelAccessToken and HuggingFace__ApiKey, then re-run.'
    return
}

# 3. Apply parameters to .env.
if ($PSBoundParameters.ContainsKey('Port')) { Set-EnvKey '.env' 'HOST_PORT' "$Port" }
if ($TunnelUrl) {
    if ($TunnelUrl -notmatch '^https://') { throw 'TunnelUrl must be an https:// URL.' }
    $TunnelUrl = $TunnelUrl.TrimEnd('/')
    Set-EnvKey '.env' 'App__PublicBaseUrl' $TunnelUrl
}

# 4. Build & run.
if ($Rebuild) { docker compose up --build -d } else { docker compose up -d }
if ($LASTEXITCODE -ne 0) { throw 'docker compose failed. Is Docker running? Is HOST_PORT free?' }

# 5. Health check on the effective host port.
$effectivePort = if ($PSBoundParameters.ContainsKey('Port')) { "$Port" } else { (Get-EnvValue '.env' 'HOST_PORT') }
if (-not $effectivePort) { $effectivePort = '8080' }
$healthUrl = "http://localhost:$effectivePort/health"
Write-Host "Waiting for $healthUrl ..."
$ok = $false
for ($i = 0; $i -lt 30; $i++) {
    try {
        if ((Invoke-WebRequest $healthUrl -TimeoutSec 3 -SkipHttpErrorCheck).StatusCode -eq 200) { $ok = $true; break }
    } catch { }
    Start-Sleep -Seconds 2
}
if ($ok) { Write-Host "Health OK: $healthUrl" -ForegroundColor Green }
else { Write-Warning 'Health check did not pass. Inspect logs: docker compose logs -f' }

# 6. Webhook (only when a tunnel URL was given).
if ($TunnelUrl) {
    if (-not (Get-Command 'line' -ErrorAction SilentlyContinue)) {
        Write-Host 'Installing the line CLI (Line.OpenApi.Tools) ...'
        dotnet tool install -g Line.OpenApi.Tools
    }
    $token = Get-EnvValue '.env' 'Line__ChannelAccessToken'
    if (-not $token) { throw 'Line__ChannelAccessToken is not set in .env.' }
    line config set default --token $token
    line webhook set-endpoint --url "$TunnelUrl/webhook"
    line webhook test-endpoint
    Write-Host ''
    Write-Host 'Webhook set. Now message your bot on LINE (chat, /image ...).' -ForegroundColor Green
} else {
    Write-Host ''
    Write-Host 'Next steps:' -ForegroundColor Cyan
    Write-Host "  1) Expose the port:  devtunnel host -p $effectivePort --allow-anonymous"
    Write-Host "  2) Re-run with the tunnel URL:  ./scripts/run.ps1 -Port $effectivePort -TunnelUrl https://<your-tunnel>"
}
