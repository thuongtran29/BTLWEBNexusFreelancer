# Tests a copy of ConfigureAi in an isolated fixture directory, never the actual local settings.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$fixture = Join-Path $PSScriptRoot ('_runtime/AiConfig-' + [guid]::NewGuid().ToString('N'))
$fixtureTools = Join-Path $fixture 'tools'
[void](New-Item -ItemType Directory -Path $fixtureTools -Force)
$tool = Join-Path $fixtureTools 'ConfigureAi.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ConfigureAi.ps1') -Destination $tool
$store = Join-Path $fixture '_local/integrations.dat'
$entropy = [Text.Encoding]::UTF8.GetBytes('NexusFreelance.integrations.v1')
$keys = @('NEXUS_AI_ENABLED', 'NEXUS_OPENAI_API_KEY', 'NEXUS_OPENAI_MODEL')
$previous = @{}
foreach ($name in $keys) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$passed = 0
function Check($condition, $message) { if (!$condition) { throw $message } }
function Pass($name) { $script:passed++; Write-Host ($name + ': PASS') }
function Read-Fixture {
    $bytes = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($store), $entropy,
        [Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { return [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json }
    finally { [Array]::Clear($bytes, 0, $bytes.Length) }
}
function Invoke-Configure([string]$value, [scriptblock]$duringPrompt = {}) {
    function Read-Host {
        param($Prompt, [switch]$AsSecureString)
        Check $AsSecureString 'Credential was requested without secure input'
        & $duringPrompt
        ConvertTo-SecureString $value -AsPlainText -Force
    }
    & $tool -Model 'fixture-model'
}
try {
    foreach ($name in $keys) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
    $status = & $tool -Status
    Check (!$status.EffectiveAiConfigured -and !$status.LocalSettingsPresent -and !$status.ProviderConnectionTested) 'Missing key was reported ready'
    Check (!(Test-Path -LiteralPath (Join-Path $fixture '_local'))) 'Status created local settings'
    Pass 'No-key status is read-only and honestly unavailable'

    Invoke-Configure 'fixture-only-key-1234567890'
    $settings = Read-Fixture
    Check ($settings.NEXUS_OPENAI_API_KEY -eq 'fixture-only-key-1234567890' -and $settings.NEXUS_AI_ENABLED -eq 'true') 'Encrypted settings did not round-trip'
    Check (![Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($store)).Contains('fixture-only-key')) 'Key stored as plaintext'
    $acl = Get-Acl -LiteralPath $store
    Check $acl.AreAccessRulesProtected 'Secret ACL inherits general permissions'
    Check (@($acl.Access | Where-Object { $_.IsInherited }).Count -eq 0) 'Unexpected inherited permission'
    Pass 'Secure prompt, DPAPI round-trip and restricted file permissions'

    # Add an unrelated integration fixture to prove rotation/disable preserve it.
    $settings | Add-Member -NotePropertyName NEXUS_GOOGLE_CLIENT_ID -NotePropertyValue 'fixture-google-id'
    $plain = [Text.Encoding]::UTF8.GetBytes(($settings | ConvertTo-Json -Compress))
    try { [IO.File]::WriteAllBytes($store, [Security.Cryptography.ProtectedData]::Protect($plain, $entropy,
        [Security.Cryptography.DataProtectionScope]::CurrentUser)) }
    finally { [Array]::Clear($plain, 0, $plain.Length) }
    Invoke-Configure 'fixture-rotated-key-1234567890'
    $settings = Read-Fixture
    Check ($settings.NEXUS_GOOGLE_CLIENT_ID -eq 'fixture-google-id' -and $settings.NEXUS_OPENAI_API_KEY -eq 'fixture-rotated-key-1234567890') 'Key rotation lost another integration'
    Pass 'Atomic key rotation preserves other integration settings'

    $before = (Get-FileHash -LiteralPath $store).Hash
    $rejected = $false
    try { Invoke-Configure 'short' } catch { $rejected = $_.Exception.Message.Contains('Invalid API key format') }
    Check ($rejected -and (Get-FileHash -LiteralPath $store).Hash -eq $before) 'Invalid key modified settings'
    Pass 'Invalid key does not replace working settings'

    $rejected = $false
    try { Invoke-Configure 'fixture-new-key-1234567890' { [IO.File]::AppendAllText($store, 'fixture-concurrent-change') } }
    catch { $rejected = $_.Exception.Message.Contains('Local settings changed') }
    Check $rejected 'Concurrent settings change was overwritten'
    Check (@(Get-ChildItem -LiteralPath (Split-Path -Parent $store) -Filter 'ai-settings-*.tmp').Count -eq 0) 'Temporary encrypted file leaked'
    Pass 'Concurrent update is rejected and temporary file cleaned'

    # Restore a valid encrypted fixture, then test disable and effective environment precedence.
    $plain = [Text.Encoding]::UTF8.GetBytes(($settings | ConvertTo-Json -Compress))
    try { [IO.File]::WriteAllBytes($store, [Security.Cryptography.ProtectedData]::Protect($plain, $entropy,
        [Security.Cryptography.DataProtectionScope]::CurrentUser)) }
    finally { [Array]::Clear($plain, 0, $plain.Length) }
    & $tool -Disable
    $settings = Read-Fixture
    Check ($settings.NEXUS_AI_ENABLED -eq 'false' -and $settings.NEXUS_GOOGLE_CLIENT_ID -eq 'fixture-google-id') 'Disable lost other settings'
    Check (!(& $tool -Status).EffectiveAiConfigured) 'Disabled AI reported configured'
    Pass 'Disable requires no credential and preserves other integrations'

    $env:NEXUS_AI_ENABLED = 'true'
    $env:NEXUS_OPENAI_API_KEY = 'fixture-environment-key-1234567890'
    $env:NEXUS_OPENAI_MODEL = 'fixture-model'
    $status = & $tool -Status
    $safeOutput = $status | ConvertTo-Json -Compress
    Check ($status.EffectiveAiConfigured -and !$status.LocalAiEnabled -and !$status.ProviderConnectionTested) 'Environment precedence differs from application'
    Check (!$safeOutput.Contains('fixture-environment-key') -and !$safeOutput.Contains('fixture-rotated-key')) 'Status exposed credentials'
    Pass 'Status reports environment precedence without credentials or provider claims'

    [IO.File]::WriteAllText($store, 'corrupted-fixture')
    $before = (Get-FileHash -LiteralPath $store).Hash
    $rejected = $false
    try { & $tool -Disable } catch { $rejected = $_.Exception.Message.Contains('Cannot read encrypted local settings') }
    Check ($rejected -and (Get-FileHash -LiteralPath $store).Hash -eq $before) 'Unreadable store was overwritten'
    Pass 'Unreadable encrypted settings fail safely without replacement'
    Write-Host "AI configuration tests: $passed passed; fixture settings only; no provider calls."
} finally {
    foreach ($name in $keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    $settings = $null
}
