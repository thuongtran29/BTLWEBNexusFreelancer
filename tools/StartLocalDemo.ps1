# Explicit local email pickup mode. Messages stay outside NexusFreelance.Web.
$ErrorActionPreference = 'Stop'
$solution = Split-Path -Parent $PSScriptRoot
$env:NEXUS_MAIL_MODE = 'pickup'
$env:NEXUS_MAIL_PICKUP_DIRECTORY = Join-Path $solution '_local\mailbox'
$env:NEXUS_PUBLIC_BASE_URL = 'https://localhost:44391'
$env:NEXUS_REQUIRE_EMAIL_VERIFICATION = 'true'
Write-Host "Local email test mode. Messages: $env:NEXUS_MAIL_PICKUP_DIRECTORY"
& (Join-Path $PSScriptRoot 'StartLocalIisExpress.ps1')
