$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'ExternalServiceSetup.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('NexusServiceSetup-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$path=Join-Path $fixture 'integrations.dat';$count=0
function Check([bool]$Ok,[string]$Name){if(!$Ok){throw $Name};$script:count++;'PASS '+$Name}
try {
    $entry=@{Origin='https://localhost:44391';Email='synthetic@gmail.com';Password='abcd efgh ijkl mnop';GeminiKey='synthetic-key-only-1234567890';GeminiModel='gemini-fixture';TmnCode='TEST0001';HashSecret=('s'*64)}
    $saved=Save-NexusExternalServices $path '' @{} $entry @('Mail')
    Check ((Test-Path -LiteralPath $path) -and $saved.Hash -and $saved.Settings['NEXUS_SMTP_PASSWORD'] -ceq 'abcdefghijklmnop') 'Gmail save verifies encrypted bytes on disk and normalizes grouped app password'
    $hash=$saved.Hash;$failed=$false
    try {Save-NexusExternalServices $path $hash $saved.Settings $entry @()|Out-Null}catch{$failed=$true}
    Check ($failed -and (Get-NexusStoreHash $path) -ceq $hash) 'No selected service cannot report success or replace credentials'
    $entry.Password='short';$failed=$false
    try {Save-NexusExternalServices $path $hash $saved.Settings $entry @('Mail')|Out-Null}catch{$failed=$true}
    Check ($failed -and (Get-NexusStoreHash $path) -ceq $hash) 'Incomplete app password preserves saved configuration'
    $entry.Password='abcdefghijklmnop'
    $json=Join-Path $fixture 'oauth.json';$entry.GoogleJson=$json
    [IO.File]::WriteAllText($json,'{"web":{"client_id":"synthetic.apps.googleusercontent.com","client_secret":"synthetic-secret","redirect_uris":["https://localhost:44391/Account/GoogleCallback"]}}')
    $saved=Save-NexusExternalServices $path $saved.Hash $saved.Settings $entry @('Google','Gemini')
    Check ($saved.Settings['NEXUS_GOOGLE_CLIENT_SECRET'] -ceq 'synthetic-secret' -and $saved.Settings['NEXUS_GEMINI_API_KEY'] -ceq 'synthetic-key-only-1234567890' -and $saved.Settings['NEXUS_SMTP_PASSWORD'] -ceq 'abcdefghijklmnop') 'Google and Gemini merge preserves Gmail password'
    $saved=Save-NexusExternalServices $path $saved.Hash $saved.Settings $entry @('VNPay')
    Check ($saved.Settings['NEXUS_VNPAY_MODE'] -ceq 'sandbox' -and !$saved.Settings.ContainsKey('NEXUS_VNPAY_LIVE_ENABLED')) 'VNPay save only enables sandbox'
    $hash=$saved.Hash;$entry.Origin='http://localhost:44391';$failed=$false
    try {Save-NexusExternalServices $path $hash $saved.Settings $entry @('Google')|Out-Null}catch{$failed=$true}
    Check ($failed -and (Get-NexusStoreHash $path) -ceq $hash) 'Unsafe HTTP callback cannot replace valid settings'
    $entry.Origin='https://localhost:44391';$entry.GeminiKey='short';$failed=$false
    try {Save-NexusExternalServices $path $hash $saved.Settings $entry @('Mail','Gemini')|Out-Null}catch{$failed=$true}
    Check ($failed -and (Get-NexusStoreHash $path) -ceq $hash) 'Invalid second service does not partially overwrite first service'
    $entry.GeminiKey='synthetic-key-only-1234567890'
    $saved=Save-NexusExternalServices $path $hash $saved.Settings $entry @('Mail','Google','Gemini','VNPay')
    Check ((Read-NexusIntegrationStore $path).Count -eq $saved.Settings.Count -and $saved.Hash -ceq (Get-NexusStoreHash $path)) 'All four services roundtrip through the exact GUI save workflow'
    # A write attempt that silently does nothing must never reach success acknowledgement.
    $absent=Join-Path $fixture 'never-written.dat'
    function Write-NexusIntegrationStore { param($Path,$Settings,$ExpectedHash) }
    $failed=$false
    try {Save-NexusExternalServices $absent '' @{} $entry @('Mail')|Out-Null}catch{$failed=$true}
    Check ($failed -and !(Test-Path -LiteralPath $absent)) 'Readback verification catches a silent no-op writer'
    'EXTERNAL_SERVICE_SETUP_CHECKS='+$count+'; synthetic inputs only; no GUI/network/database interactions'
} finally {
    $absolute=[IO.Path]::GetFullPath($fixture);$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    if([IO.Path]::GetDirectoryName($absolute) -cne $parent -or [IO.Path]::GetFileName($absolute) -notmatch '^NexusServiceSetup-[0-9a-f]{32}$' -or ((Get-Item -LiteralPath $absolute).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Unsafe setup fixture cleanup path'}
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
