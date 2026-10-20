#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('NexusConfigTest-' + [guid]::NewGuid().ToString('N'))
$testTools = Join-Path $testRoot 'tools'
[IO.Directory]::CreateDirectory($testTools) | Out-Null
$testScript = Join-Path $testTools 'ConfigureIntegrations.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ConfigureIntegrations.ps1') -Destination $testScript
function Read-Host {
    param([string]$Prompt, [switch]$AsSecureString)
    if ($AsSecureString) {
        $testSecret = New-Object Security.SecureString
        foreach ($character in 'synthetic-secret-for-tests'.ToCharArray()) { $testSecret.AppendChar($character) }
        $testSecret.MakeReadOnly()
        return $testSecret
    }
    switch -Exact ($Prompt) {
        'Google Web application client ID' { return 'synthetic.apps.googleusercontent.com' }
        'Exact HTTPS callback (local: https://localhost:44391/Account/GoogleCallback)' { return 'https://localhost:44391/Account/GoogleCallback' }
        'SMTP host (for example smtp.gmail.com)' { return 'smtp.example.test' }
        'SMTP STARTTLS port (usually 587)' { return '587' }
        'From email address (authorized by your mail provider)' { return 'sender@example.test' }
        'SMTP username (usually the full email address)' { return 'sender@example.test' }
        'Public HTTPS origin (local: https://localhost:44391)' { return 'https://localhost:44391' }
        default { throw 'Unexpected configuration prompt.' }
    }
}
try {
    & $testScript -Service Google | Out-Null
    & $testScript -Service Mail | Out-Null
    & $testScript -Service Mail | Out-Null
    $testConfig = Join-Path $testRoot '_local\integrations.dat'
    $testPlain = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($testConfig), [Text.Encoding]::UTF8.GetBytes('NexusFreelance.integrations.v1'), [Security.Cryptography.DataProtectionScope]::CurrentUser)
    try {
        $testSettings = [Text.Encoding]::UTF8.GetString($testPlain) | ConvertFrom-Json
        if ($testSettings.NEXUS_GOOGLE_CLIENT_ID -cne 'synthetic.apps.googleusercontent.com' -or
            $testSettings.NEXUS_GOOGLE_CLIENT_SECRET -cne 'synthetic-secret-for-tests' -or
            $testSettings.NEXUS_MAIL_MODE -cne 'smtp' -or
            $testSettings.NEXUS_SMTP_HOST -cne 'smtp.example.test' -or
            $testSettings.NEXUS_SMTP_PASSWORD -cne 'synthetic-secret-for-tests') { throw 'Configuration merge did not preserve expected values.' }
        if (!(Get-Acl -LiteralPath $testConfig).AreAccessRulesProtected) { throw 'Credential file ACL must disable inheritance.' }
        'PASS: initial encrypted save, Google + SMTP merge, repeated atomic update, DPAPI roundtrip, private ACL.'
    } finally { [Array]::Clear($testPlain, 0, $testPlain.Length) }
} finally {
    Remove-Item -LiteralPath Function:\Read-Host
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolvedTestRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedTestRoot).StartsWith('NexusConfigTest-')) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
