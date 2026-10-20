param([string]$WebRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'NexusFreelance.Web'))
$ErrorActionPreference = 'Stop'
$web = (Resolve-Path -LiteralPath $WebRoot).Path
$root = Split-Path -Parent $web
$runtime = Join-Path $root '_local/jobs/reminders'
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
# Hold across compile, config and execution so concurrent task launches cannot overwrite files.
$mutex = New-Object Threading.Mutex($false, ('Local\NexusReminders-' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($web)).Replace('/','_').Replace('\','_')))
if (!$mutex.WaitOne(0)) { $mutex.Dispose(); Write-Output 'Worker already running.'; exit 0 }
try {
    [xml]$config = Get-Content -LiteralPath (Join-Path $web 'Web.config') -Raw
    $db = @($config.configuration.connectionStrings.add | Where-Object name -eq 'NexusDb')
    if ($db.Count -ne 1 -or $db[0].providerName -ne 'System.Data.SqlClient') { throw 'NexusDb configuration required.' }
    $exe = Join-Path $runtime 'ReminderWorker.exe'
    $source = Join-Path $PSScriptRoot 'ReminderWorker.cs'
    Get-ChildItem -LiteralPath (Join-Path $web 'bin') -Filter '*.dll' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $runtime -Force }
    $compiler = Join-Path $env:windir 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    & $compiler /nologo /target:exe "/out:$exe" "/reference:$runtime/NexusFreelance.Web.dll" "/reference:$runtime/EntityFramework.dll" /reference:System.Data.dll $source
    if ($LASTEXITCODE -ne 0) { throw 'Worker compilation failed.' }
    $connection = [Security.SecurityElement]::Escape([string]$db[0].connectionString)
    # Include EF provider config, not web authentication or integration secrets.
    $workerConfig = '<configuration><configSections><section name="entityFramework" type="System.Data.Entity.Internal.ConfigFile.EntityFrameworkSection, EntityFramework" requirePermission="false" /></configSections><connectionStrings><add name="NexusDb" providerName="System.Data.SqlClient" connectionString="' + $connection + '" /></connectionStrings>' + $config.configuration.entityFramework.OuterXml + '</configuration>'
    [IO.File]::WriteAllText($exe + '.config', $workerConfig, (New-Object Text.UTF8Encoding($false)))
    $output = & $exe
    $code = $LASTEXITCODE
    $log = Join-Path $runtime ('runs-' + (Get-Date -Format 'yyyy-MM') + '.ndjson')
    $output | Add-Content -LiteralPath $log -Encoding UTF8
    $output | Write-Output
    if ($code -ne 0) { throw 'Reminder worker failed. Inspect the local structured run log and SQL availability.' }
} finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
