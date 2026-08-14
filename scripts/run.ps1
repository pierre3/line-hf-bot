#requires -Version 7
<#
.SYNOPSIS
    One command to run the containerized bot and (optionally) manage the Dev Tunnel.
    Trusts the host root CAs (corporate proxy), builds/runs the container, health-checks it,
    starts a persistent Dev Tunnel in the background, and sets + verifies the LINE webhook.

.PARAMETER Port
    Host port to expose (written to HOST_PORT in .env). Defaults to the current HOST_PORT, else 8080.

.PARAMETER StartTunnel
    Manage a persistent Dev Tunnel: create it if missing, host it in the background (no separate
    terminal needed), derive its stable URL, and use it for App__PublicBaseUrl + the webhook.
    Requires a prior `devtunnel user login`.

.PARAMETER TunnelName
    Persistent Dev Tunnel id used with -StartTunnel. Default: line-hf-bot.

.PARAMETER TunnelUrl
    Use this tunnel URL explicitly instead of -StartTunnel (e.g. a tunnel you host yourself).

.PARAMETER Rebuild
    Rebuild the image (docker compose up --build).

.PARAMETER ExportCerts
    Force re-exporting the host root CAs into ./certs.

.EXAMPLE
    # everything in one command (tunnel hosted in the background):
    ./scripts/run.ps1 -Port 8081 -StartTunnel -Rebuild

.EXAMPLE
    # just build/run/health-check:
    ./scripts/run.ps1
#>
[CmdletBinding()]
param(
    [int]$Port,
    [switch]$StartTunnel,
    [string]$TunnelName = 'line-hf-bot',
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

function Get-TunnelUrl([string]$Name, [string]$HostPort) {
    # devtunnel show doesn't print the URL, but tunnelId is "<name>.<cluster>" (e.g. line-hf-bot.jpe1)
    # and the per-port forwarding URL is https://<name>-<port>.<cluster>.devtunnels.ms
    $json = (& devtunnel show $Name --json 2>$null | Out-String)
    if (-not $json) { return $null }
    try { $obj = $json | ConvertFrom-Json } catch { return $null }
    $tunnelId = $obj.tunnel.tunnelId
    if (-not $tunnelId) { return $null }
    $parts = $tunnelId -split '\.', 2
    if ($parts.Count -lt 2) { return $null }
    return "https://$($parts[0])-$HostPort.$($parts[1]).devtunnels.ms"
}

function Test-TunnelHosted([string]$Name) {
    $json = (& devtunnel show $Name --json 2>$null | Out-String)
    if (-not $json) { return $false }
    try { $obj = $json | ConvertFrom-Json } catch { return $false }
    return ([int]$obj.tunnel.hostConnections) -gt 0
}

# 1. Trust host root CAs (corporate TLS-inspecting proxy). No-op on normal networks.
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

# 2. Ensure .env exists and is filled in.
if (-not (Test-Path '.env')) {
    Copy-Item '.env.example' '.env'
    Write-Warning 'Created .env from .env.example. Fill in Line__ChannelSecret, Line__ChannelAccessToken and HuggingFace__ApiKey, then re-run.'
    return
}

# 3. Host port.
if ($PSBoundParameters.ContainsKey('Port')) { Set-EnvKey '.env' 'HOST_PORT' "$Port" }
$effectivePort = if ($PSBoundParameters.ContainsKey('Port')) { "$Port" } else { (Get-EnvValue '.env' 'HOST_PORT') }
if (-not $effectivePort) { $effectivePort = '8080' }

# 4. Dev Tunnel (persistent, hosted in the background).
if ($StartTunnel) {
    $who = (& devtunnel user show 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or $who -match 'not logged in') {
        throw 'Not logged in to Dev Tunnels. Run: devtunnel user login'
    }

    & devtunnel show $TunnelName 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Creating persistent tunnel '$TunnelName' ..."
        devtunnel create $TunnelName --allow-anonymous | Out-Null
    }
    devtunnel port create $TunnelName -p $effectivePort --protocol http 2>&1 | Out-Null  # ignore if it exists
    devtunnel access create $TunnelName --anonymous 2>&1 | Out-Null                        # ensure anonymous

    $TunnelUrl = Get-TunnelUrl $TunnelName $effectivePort
    if (-not $TunnelUrl) { throw "Could not determine the tunnel URL. Check: devtunnel show $TunnelName" }
    Write-Host "Tunnel URL: $TunnelUrl"

    if (Test-TunnelHosted $TunnelName) {
        Write-Host "Tunnel '$TunnelName' is already hosted."
    } else {
        Write-Host 'Starting the tunnel host in the background ...'
        Start-Process devtunnel -ArgumentList 'host', $TunnelName -WindowStyle Hidden
        $hosted = $false
        for ($i = 0; $i -lt 15; $i++) {
            Start-Sleep -Seconds 2
            if (Test-TunnelHosted $TunnelName) { $hosted = $true; break }
        }
        if ($hosted) { Write-Host 'Tunnel host is up.' }
        else { Write-Warning "Tunnel host did not come up. Try manually: devtunnel host $TunnelName" }
    }
}

# 5. Persist the public base URL (used to build image URLs LINE fetches).
if ($TunnelUrl) {
    if ($TunnelUrl -notmatch '^https://') { throw 'TunnelUrl must be an https:// URL.' }
    $TunnelUrl = $TunnelUrl.TrimEnd('/')
    Set-EnvKey '.env' 'App__PublicBaseUrl' $TunnelUrl
}

# 6. Build & run.
if ($Rebuild) { docker compose up --build -d } else { docker compose up -d }
if ($LASTEXITCODE -ne 0) { throw 'docker compose failed. Is Docker running? Is HOST_PORT free?' }

# 7. Health check.
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

# 8. Webhook (when we have a tunnel URL).
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
    Write-Host 'Ready. Message your bot on LINE (chat, /image ...).' -ForegroundColor Green
} else {
    Write-Host ''
    Write-Host 'Next: expose the port and set the webhook, e.g.:' -ForegroundColor Cyan
    Write-Host "  ./scripts/run.ps1 -Port $effectivePort -StartTunnel"
}
