#requires -Version 7
<#
.SYNOPSIS
    Point the LINE webhook at your tunnel and verify it.
    Sets App__PublicBaseUrl in .env, recreates the container so it takes effect,
    then uses the `line` CLI to set and test the webhook endpoint.

.PARAMETER TunnelUrl
    The public HTTPS base URL of your tunnel (e.g. https://abcd-8081.jp.devtunnels.ms).

.EXAMPLE
    ./scripts/set-webhook.ps1 -TunnelUrl https://abcd-8081.jp.devtunnels.ms
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TunnelUrl
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ($TunnelUrl -notmatch '^https://') { throw 'TunnelUrl must be an https:// URL.' }
$TunnelUrl = $TunnelUrl.TrimEnd('/')

# 1. Set App__PublicBaseUrl in .env (used to build the image URLs LINE fetches).
$lines = Get-Content '.env'
if ($lines -match '^App__PublicBaseUrl=') {
    $lines = $lines -replace '^App__PublicBaseUrl=.*', "App__PublicBaseUrl=$TunnelUrl"
} else {
    $lines += "App__PublicBaseUrl=$TunnelUrl"
}
Set-Content '.env' $lines
Write-Host "Set App__PublicBaseUrl=$TunnelUrl"

# 2. Recreate the container so the new env is applied.
docker compose up -d
if ($LASTEXITCODE -ne 0) { throw 'docker compose failed.' }

# 3. Ensure the line CLI is available.
if (-not (Get-Command 'line' -ErrorAction SilentlyContinue)) {
    Write-Host 'Installing the line CLI (Line.OpenApi.Tools) ...'
    dotnet tool install -g Line.OpenApi.Tools
}

# 4. Set and verify the webhook using the channel access token from .env.
$tokenMatch = Select-String -Path '.env' -Pattern '^Line__ChannelAccessToken=(.+)$'
if (-not $tokenMatch) { throw 'Line__ChannelAccessToken is not set in .env.' }
$token = $tokenMatch.Matches[0].Groups[1].Value

line config set default --token $token
line webhook set-endpoint --url "$TunnelUrl/webhook"
line webhook test-endpoint

Write-Host ''
Write-Host 'Webhook set. Now message your bot on LINE.' -ForegroundColor Green
