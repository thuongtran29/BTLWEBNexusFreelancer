#requires -Version 5.1
[CmdletBinding()]
param([ValidateSet('Google', 'Mail', 'Status')][string]$Service = 'Status')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$solutionRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$privateDirectory = Join-Path $solutionRoot '_local'
$configFile = Join-Path $privateDirectory 'integrations.dat'
$storeHelper = Join-Path $PSScriptRoot 'IntegrationStore.ps1'
if (Test-Path -LiteralPath $storeHelper) {
    . $storeHelper
    $configFile = Get-NexusIntegrationStorePath -SolutionRoot (Split-Path -Parent $PSScriptRoot)
    $privateDirectory = Split-Path -Parent $configFile
}
$entropy = [Text.Encoding]::UTF8.GetBytes('NexusFreelance.integrations.v1')
$scope = [Security.Cryptography.DataProtectionScope]::CurrentUser
$settings = @{}
foreach ($entry in @($privateDirectory, $configFile)) {
    if ((Test-Path -LiteralPath $entry) -and ((Get-Item -LiteralPath $entry -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Refusing a redirected configuration directory or file.'
    }
}
if (Test-Path -LiteralPath $configFile) {
    $plainBytes = $null
    try {
        if ((Get-Item -LiteralPath $configFile -Force).Length -gt 65536) { throw 'Configuration is too large.' }
        $plainBytes = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($configFile), $entropy, $scope)
        $dictionary = [Text.Encoding]::UTF8.GetString($plainBytes) | ConvertFrom-Json
        if ($null -eq $dictionary -or $dictionary -isnot [Management.Automation.PSCustomObject]) { throw 'Invalid dictionary.' }
        foreach ($property in $dictionary.PSObject.Properties) {
            if ($property.Value -isnot [string]) { throw 'Invalid dictionary value.' }
            $settings[$property.Name] = $property.Value
        }
    } catch { throw 'Cannot decrypt or read the existing configuration. Nothing was overwritten. Use the original Windows account.' }
    finally { if ($null -ne $plainBytes) { [Array]::Clear($plainBytes, 0, $plainBytes.Length) } }
}
$keys = @('NEXUS_GOOGLE_CLIENT_ID', 'NEXUS_GOOGLE_CLIENT_SECRET', 'NEXUS_GOOGLE_REDIRECT_URI',
    'NEXUS_MAIL_MODE', 'NEXUS_SMTP_HOST', 'NEXUS_SMTP_PORT', 'NEXUS_SMTP_FROM',
    'NEXUS_SMTP_USERNAME', 'NEXUS_SMTP_PASSWORD', 'NEXUS_PUBLIC_BASE_URL')
if ($Service -eq 'Status') {
    foreach ($key in $keys) {
        $environmentPresent = ![string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($key))
        $filePresent = $settings.ContainsKey($key) -and ![string]::IsNullOrWhiteSpace($settings[$key])
        $source = if ($environmentPresent) { 'environment' } elseif ($filePresent) { 'protected file' } else { 'absent' }
        Write-Output ('{0}: {1}' -f $key, $source)
    }
    return
}
function Read-Required([string]$Prompt) {
    $value = (Read-Host $Prompt).Trim()
    if ([string]::IsNullOrWhiteSpace($value) -or $value -match '[\r\n]') { throw 'A required value is empty or invalid.' }
    return $value
}
function Read-Secret([string]$Prompt) {
    $secure = Read-Host $Prompt -AsSecureString
    $pointer = [IntPtr]::Zero
    try {
        if ($secure.Length -eq 0) { throw 'A secret is required.' }
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    } finally {
        if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
        $secure.Dispose()
    }
}
function Read-Https([string]$Prompt, [bool]$Callback) {
    $value = Read-Required $Prompt
    $uri = $null
    if (![Uri]::TryCreate($value, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'https' -or
        !$uri.Host -or $uri.UserInfo -or $value -match '[\s?#\\@]') { throw 'Use an HTTPS URL without credentials, query or fragment.' }
    $tail = $value.Substring($value.IndexOf('://') + 3)
    $slash = $tail.IndexOf('/')
    $path = if ($slash -lt 0) { '' } else { $tail.Substring($slash) }
    if ($Callback) {
        if ($path -cne '/Account/GoogleCallback') { throw 'Callback path must be exactly /Account/GoogleCallback.' }
        return $value
    }
    if ($path -ne '' -and $path -ne '/') { throw 'Public base must be an HTTPS origin without a path.' }
    return $uri.GetLeftPart([UriPartial]::Authority)
}
if ($Service -eq 'Google') {
    $settings['NEXUS_GOOGLE_CLIENT_ID'] = Read-Required 'Google Web application client ID'
    $settings['NEXUS_GOOGLE_REDIRECT_URI'] = Read-Https 'Exact HTTPS callback (local: https://localhost:44391/Account/GoogleCallback)' $true
    $settings['NEXUS_GOOGLE_CLIENT_SECRET'] = Read-Secret 'Google client secret'
} else {
    $hostName = Read-Required 'SMTP host (for example smtp.gmail.com)'
    if ([Uri]::CheckHostName($hostName) -eq [UriHostNameType]::Unknown) { throw 'Use a host name or IP address without a scheme or port.' }
    $port = 0
    if (![int]::TryParse((Read-Required 'SMTP STARTTLS port (usually 587)'), [ref]$port) -or $port -lt 1 -or $port -gt 65535) { throw 'SMTP port must be 1-65535.' }
    $from = Read-Required 'From email address (authorized by your mail provider)'
    try { $address = [Net.Mail.MailAddress]::new($from) } catch { throw 'Invalid From email address.' }
    if ($address.Address -ne $from) { throw 'Enter only the From email address, without a display name.' }
    $settings['NEXUS_SMTP_HOST'] = $hostName
    $settings['NEXUS_SMTP_PORT'] = [string]$port
    $settings['NEXUS_SMTP_FROM'] = $from
    $settings['NEXUS_SMTP_USERNAME'] = Read-Required 'SMTP username (usually the full email address)'
    $settings['NEXUS_PUBLIC_BASE_URL'] = Read-Https 'Public HTTPS origin (local: https://localhost:44391)' $false
    $settings['NEXUS_SMTP_PASSWORD'] = Read-Secret 'SMTP password or provider app password'
    $settings['NEXUS_MAIL_MODE'] = 'smtp'
}
$temporary = $null
$plainBytes = $null
$saveStage = 'encrypt'
function Set-PrivateFileAcl([string]$Path) {
    $privateAcl = [Security.AccessControl.FileSecurity]::new()
    $privateAcl.SetAccessRuleProtection($true, $false)
    $userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    foreach ($sid in @($userSid, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'), [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))) {
        $privateAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'Allow'))
    }
    # Apply only the modified access rules. Set-Acl can request the audit
    # privilege when updating an already protected file on some Windows hosts.
    $fileInfo = [IO.FileInfo]::new($Path)
    if ($fileInfo.PSObject.Methods['SetAccessControl']) {
        $fileInfo.SetAccessControl($privateAcl)
    } else {
        [IO.FileSystemAclExtensions]::SetAccessControl($fileInfo, $privateAcl)
    }
}
try {
    $plainBytes = [Text.Encoding]::UTF8.GetBytes(($settings | ConvertTo-Json -Compress))
    $encryptedBytes = [Security.Cryptography.ProtectedData]::Protect($plainBytes, $entropy, $scope)
    if ($encryptedBytes.Length -gt 65536) { throw 'Configuration is too large for the application reader.' }
    $saveStage = 'prepare directory'
    if (!(Test-Path -LiteralPath $privateDirectory)) { [IO.Directory]::CreateDirectory($privateDirectory) | Out-Null }
    $saveStage = 'prepare permissions'
    $temporary = Join-Path $privateDirectory ([guid]::NewGuid().ToString('N') + '.tmp')
    $saveStage = 'write encrypted file'
    [IO.File]::WriteAllBytes($temporary, $encryptedBytes)
    $saveStage = 'secure temporary file'
    Set-PrivateFileAcl $temporary
    if (Test-Path -LiteralPath $configFile) {
        $saveStage = 'secure existing file'
        Set-PrivateFileAcl $configFile
        $saveStage = 'replace existing file'
        # PowerShell converts $null to an empty string for this .NET string
        # argument. NullString keeps the backup path null for atomic updates.
        [IO.File]::Replace($temporary, $configFile, [System.Management.Automation.Language.NullString]::Value)
    } else { [IO.File]::Move($temporary, $configFile) }
    Write-Output 'Encrypted settings saved for this Windows user. No service connection was tested. Restart IIS Express to apply.'
} catch { throw ('Could not save protected settings at step "{0}" ({1}). No plaintext credential file was written.' -f $saveStage, $_.Exception.GetType().Name) }
finally {
    if ($null -ne $plainBytes) { [Array]::Clear($plainBytes, 0, $plainBytes.Length) }
    if ($temporary -and (Test-Path -LiteralPath $temporary)) { Remove-Item -LiteralPath $temporary -Force }
    $settings.Clear()
}
