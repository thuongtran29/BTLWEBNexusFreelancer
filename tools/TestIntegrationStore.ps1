$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'IntegrationStore.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('NexusIntegrationStore-'+[guid]::NewGuid().ToString('N'))
$count=0
function Check([bool]$Ok,[string]$Name){if(!$Ok){throw $Name};$script:count++;'PASS '+$Name}
try {
    $web=Join-Path $fixture 'NexusFreelance.Web';[void][IO.Directory]::CreateDirectory($web)
    $persistent=Join-Path $fixture 'user/integrations.dat'
    [IO.File]::WriteAllText((Join-Path $web 'Web.config'),('<configuration><appSettings><add key="NexusIntegrationStore" value="'+[Security.SecurityElement]::Escape($persistent)+'" /></appSettings></configuration>'))
    Check ((Get-NexusIntegrationStorePath $fixture) -ceq $persistent) 'Setup resolves the same persistent path as Visual Studio'
    Check ((Get-NexusIntegrationStorePath $fixture -ForWrite) -ceq (Join-Path $fixture '_local/integrations.dat')) 'Interactive setup writes beside the project, outside web root, instead of depending on launch-specific AppData visibility'
    $other=Join-Path $fixture 'another-copy';[void][IO.Directory]::CreateDirectory((Join-Path $other 'NexusFreelance.Web'))
    Copy-Item -LiteralPath (Join-Path $web 'Web.config') -Destination (Join-Path $other 'NexusFreelance.Web/Web.config')
    Check ((Get-NexusIntegrationStorePath $other) -ceq $persistent) 'Another project copy uses the same persistent user store'
    Write-NexusIntegrationStore $persistent @{NEXUS_SMTP_PASSWORD='synthetic-only';NEXUS_MAIL_MODE='smtp'} ''
    $settings=Read-NexusIntegrationStore $persistent
    Check ($settings['NEXUS_SMTP_PASSWORD'] -ceq 'synthetic-only') 'DPAPI roundtrip'
    Check (![Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($persistent)).Contains('synthetic-only')) 'No plaintext credential on disk'
    Check ((Get-Acl -LiteralPath $persistent).AreAccessRulesProtected) 'File permissions disable inheritance'
    $hash=Get-NexusStoreHash $persistent
    $settings['NEXUS_GEMINI_MODEL']='gemini-fixture';Write-NexusIntegrationStore $persistent $settings $hash
    $settings=Read-NexusIntegrationStore $persistent
    Check ($settings['NEXUS_SMTP_PASSWORD'] -ceq 'synthetic-only' -and $settings['NEXUS_GEMINI_MODEL'] -ceq 'gemini-fixture') 'Saving AI preserves SMTP credentials'
    $hash=Get-NexusStoreHash $persistent;$failed=$false
    try {Write-NexusIntegrationStore $persistent @{NEXUS_MAIL_MODE='pickup'} 'stale-hash'}catch{$failed=$true}
    Check ($failed -and (Get-NexusStoreHash $persistent) -ceq $hash) 'Concurrent change refuses overwrite'
    Check (@(Get-ChildItem (Split-Path -Parent $persistent) -Filter 'settings-*.tmp').Count -eq 0) 'Failed save removes encrypted temporary file'
    $legacy=Join-Path $fixture '_local/integrations.dat';[void][IO.Directory]::CreateDirectory((Split-Path -Parent $legacy))
    [IO.File]::WriteAllText($legacy,'deliberately-corrupt')
    Check ((Get-NexusIntegrationStorePath $fixture) -ceq $legacy) 'Explicit project store wins over shared store'
    $failed=$false;try {Read-NexusIntegrationStore $legacy|Out-Null}catch{$failed=$true}
    Check ($failed -and [IO.File]::ReadAllText($legacy) -ceq 'deliberately-corrupt') 'Corrupt explicit settings fail safely without replacement'
    Remove-Item -LiteralPath $legacy
    Check ((Get-NexusIntegrationStorePath $fixture) -ceq $persistent) 'Removing override restores persistent settings'
    'INTEGRATION_STORE_CHECKS='+$count
} finally {
    $absolute=[IO.Path]::GetFullPath($fixture)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    if([IO.Path]::GetDirectoryName($absolute) -cne $parent -or [IO.Path]::GetFileName($absolute) -notmatch '^NexusIntegrationStore-[0-9a-f]{32}$' -or ((Get-Item -LiteralPath $absolute).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Unsafe fixture cleanup path'}
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
