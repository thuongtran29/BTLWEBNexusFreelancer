param([Parameter(Mandatory=$true)][string]$AuditWebRoot,[Parameter(Mandatory=$true)][string]$FixtureJson)
$ErrorActionPreference='Stop'
$web=[IO.Path]::GetFullPath($AuditWebRoot)
$config=Join-Path $web 'Web.config'
if([IO.File]::ReadAllText($config) -notmatch 'Initial Catalog=NexusFreelanceAudit20261019;'){throw 'Isolated audit DB only'}
if($web.StartsWith('D:\FreelancerBTLWEB',[StringComparison]::OrdinalIgnoreCase)){throw 'Cannot run in delivered web root'}
$root=Split-Path -Parent $PSScriptRoot
$bin=Join-Path $root 'NexusFreelance.Web/bin'
$runtime=Join-Path $PSScriptRoot ('_runtime/PspAudit-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
Get-ChildItem -LiteralPath $bin -Filter '*.dll' | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $runtime}
$exe=Join-Path $runtime 'TestPspRefunds.exe'
Copy-Item -LiteralPath $config -Destination ($exe+'.config')
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$csc=Join-Path $vs 'MSBuild/Current/Bin/Roslyn/csc.exe'
& $csc /nologo /codepage:65001 /target:exe "/out:$exe" "/reference:$runtime/NexusFreelance.Web.dll" "/reference:$runtime/EntityFramework.dll" /reference:System.Net.Http.dll /reference:System.Data.dll /reference:System.Web.Extensions.dll /reference:System.ComponentModel.DataAnnotations.dll (Join-Path $PSScriptRoot 'TestPspRefunds.cs')
if($LASTEXITCODE -ne 0){throw 'Refund audit harness compile failed'}
$fixture=Get-Content -LiteralPath $FixtureJson -Raw | ConvertFrom-Json
if($fixture.adminId -le 0 -or $fixture.paymentIds.Count -ne 3 -or @($fixture.paymentIds|Where-Object{$_ -le 0}).Count){throw 'Three positive fixture payment IDs required'}
$env:NEXUS_VNPAY_ENABLED='true';$env:NEXUS_VNPAY_MODE='sandbox';$env:NEXUS_VNPAY_TMN_CODE='AUDIT020'
$env:NEXUS_VNPAY_HASH_SECRET='audit-20261020-vnpay-only-not-a-real-secret-0001'
$env:NEXUS_PUBLIC_BASE_URL='https://localhost:44392'
& $exe '--audit-fixtures' $fixture.adminId $fixture.paymentIds[0] $fixture.paymentIds[1] $fixture.paymentIds[2]
if($LASTEXITCODE -ne 0){throw 'Refund audit scenario failed'}
