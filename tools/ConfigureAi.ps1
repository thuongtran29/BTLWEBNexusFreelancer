[CmdletBinding(DefaultParameterSetName='Configure')]
param(
    [Parameter(Mandatory=$true, ParameterSetName='Configure')]
    [ValidatePattern('\A[A-Za-z0-9._:-]{1,33}\z')][string]$Model,
    [Parameter(Mandatory=$true, ParameterSetName='Status')][switch]$Status,
    [Parameter(Mandatory=$true, ParameterSetName='Disable')][switch]$Disable
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$solution = Split-Path -Parent $PSScriptRoot
$private = Join-Path $solution '_local'
$path = Join-Path $private 'integrations.dat'
$storeHelper = Join-Path $PSScriptRoot 'IntegrationStore.ps1'
if (Test-Path -LiteralPath $storeHelper) {
    . $storeHelper
    $path = Get-NexusIntegrationStorePath -SolutionRoot (Split-Path -Parent $PSScriptRoot)
    $private = Split-Path -Parent $path
}
$entropy = [Text.Encoding]::UTF8.GetBytes('NexusFreelance.integrations.v1')
$settings = @{}
$secret = $null
$pointer = [IntPtr]::Zero
$temporary = $null
function Get-ProtectedStoreHash([string]$filePath) {
    if (![IO.File]::Exists($filePath)) { return $null }
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash([IO.File]::ReadAllBytes($filePath))).Replace('-', '') }
    finally { $algorithm.Dispose() }
}
function Set-AiFileAccess([string]$filePath, [Security.AccessControl.FileSecurity]$access) {
    # Set-Acl can request audit privileges on replacement. Apply only the DACL through .NET.
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        [IO.FileSystemAclExtensions]::SetAccessControl([IO.FileInfo]::new($filePath), $access)
    } else { [IO.File]::SetAccessControl($filePath, $access) }
}
try {
    $before = Get-ProtectedStoreHash $path
    if (Test-Path -LiteralPath $path) {
        try {
            if ((Get-Item -LiteralPath $path).Length -gt 65536) { throw 'Oversize settings' }
            $clear = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($path), $entropy,
                [Security.Cryptography.DataProtectionScope]::CurrentUser)
            try {
                $stored = [Text.Encoding]::UTF8.GetString($clear) | ConvertFrom-Json
                if ($null -eq $stored -or $stored -isnot [pscustomobject]) { throw 'Invalid settings' }
                foreach ($property in $stored.PSObject.Properties) {
                    if ($null -ne $property.Value -and $property.Value -isnot [string]) { throw 'Invalid setting value' }
                    $settings[$property.Name] = [string]$property.Value
                }
            } finally { [Array]::Clear($clear, 0, $clear.Length) }
        } catch {
            throw 'Cannot read encrypted local settings for this Windows account. Existing settings were not changed. Run under the web application account or restore a valid local settings file.'
        }
    }
    $keys = @('NEXUS_AI_ENABLED', 'NEXUS_AI_PROVIDER', 'NEXUS_OPENAI_API_KEY', 'NEXUS_OPENAI_MODEL')
    $overrides = @($keys | Where-Object { ![string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_, 'Process')) })
    if ($Status) {
        $effective = @{}
        foreach ($name in $keys) {
            $value = [Environment]::GetEnvironmentVariable($name, 'Process')
            $effective[$name] = if ([string]::IsNullOrWhiteSpace($value)) { $settings[$name] } else { $value }
        }
        # Presence/configuration only: never print credentials, validate an account or send HTTP.
        [pscustomobject]@{
            LocalSettingsPresent = Test-Path -LiteralPath $path
            LocalAiEnabled = $settings['NEXUS_AI_ENABLED'] -eq 'true'
            EffectiveAiConfigured = ($effective['NEXUS_AI_ENABLED'] -eq 'true' -and ([string]::IsNullOrWhiteSpace($effective['NEXUS_AI_PROVIDER']) -or $effective['NEXUS_AI_PROVIDER'] -eq 'OpenAI') -and
                ![string]::IsNullOrWhiteSpace($effective['NEXUS_OPENAI_API_KEY']) -and
                $effective['NEXUS_OPENAI_MODEL'] -cmatch '\A[A-Za-z0-9._:-]{1,33}\z')
            EnvironmentOverrideNames = $overrides -join ', '
            ProviderConnectionTested = $false
        }
        $effective.Clear()
        return
    }

    # Detect updates while the operator enters the key, preserving other integrations.
    if ($Disable) { $settings['NEXUS_AI_ENABLED'] = 'false' }
    else {
        $secret = Read-Host 'OpenAI API key (hidden input; do not send it in chat)' -AsSecureString
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
        $key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        if ($key.Length -lt 20 -or $key.Length -gt 512 -or $key -match '\s') {
            throw 'Invalid API key format. Configuration was not saved.'
        }
        $settings['NEXUS_OPENAI_API_KEY'] = $key
        $settings['NEXUS_OPENAI_MODEL'] = $Model
        $settings['NEXUS_AI_ENABLED'] = 'true'
        $settings['NEXUS_AI_PROVIDER'] = 'OpenAI'
    }
    [void](New-Item -ItemType Directory -Path $private -Force)
    $bytes = [Text.Encoding]::UTF8.GetBytes(($settings | ConvertTo-Json -Compress))
    try {
        $encrypted = [Security.Cryptography.ProtectedData]::Protect($bytes, $entropy,
            [Security.Cryptography.DataProtectionScope]::CurrentUser)
    } finally { [Array]::Clear($bytes, 0, $bytes.Length) }
    if ($encrypted.Length -gt 65536) { throw 'Encrypted settings exceed the supported size. Configuration was not saved.' }
    $temporary = Join-Path $private ('ai-settings-' + [guid]::NewGuid().ToString('N') + '.tmp')
    [IO.File]::WriteAllBytes($temporary, $encrypted)
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = New-Object Security.AccessControl.FileSecurity
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($identity in @($sid, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'),
        [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity, 'FullControl', 'Allow'))
    }
    Set-AiFileAccess $temporary $acl
    $now = Get-ProtectedStoreHash $path
    if ($before -ne $now) { throw 'Local settings changed during entry. Configuration was not saved; run the tool again.' }
    if (Test-Path -LiteralPath $path) {
        Set-AiFileAccess $path $acl
        [IO.File]::Replace($temporary, $path, [NullString]::Value)
    } else { [IO.File]::Move($temporary, $path) }
    $temporary = $null
    Write-Host $(if ($Disable) { 'Local AI setting disabled; other integration settings preserved.' }
        else { 'Encrypted AI settings saved. Provider access has not been tested. Open Job Studio and explicitly consent before using OpenAI.' })
    if ($overrides.Count -gt 0) { Write-Warning ('Environment values take precedence over this file: ' + ($overrides -join ', ') + '. The web process may have different environment values.') }
} finally {
    if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    if ($null -ne $secret) { $secret.Dispose() }
    $key = $null
    $settings.Clear()
    if ($null -ne $temporary -and (Test-Path -LiteralPath $temporary)) { Remove-Item -LiteralPath $temporary -Force }
}
