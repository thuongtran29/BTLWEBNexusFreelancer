param([Parameter(Mandatory=$true)][string]$AuditWebRoot,[Parameter(Mandatory=$true)][string]$FixtureJson)
$ErrorActionPreference='Stop'
$web=[IO.Path]::GetFullPath($AuditWebRoot)
if([IO.File]::ReadAllText((Join-Path $web 'Web.config')) -notmatch 'Initial Catalog=NexusFreelanceAudit20261019'){throw 'Audit database only'}
$bin=Join-Path $web 'bin';$exe=Join-Path $bin 'TestStaffingFreshness.exe';$source=Join-Path $PSScriptRoot 'TestStaffingFreshness.cs'
& "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe" /nologo /codepage:65001 /target:exe "/out:$exe" "/reference:$bin/NexusFreelance.Web.dll" "/reference:$bin/EntityFramework.dll" /reference:System.ComponentModel.DataAnnotations.dll /reference:System.Data.dll $source
if($LASTEXITCODE -ne 0){throw 'Compile failed'}
Copy-Item (Join-Path $web 'Web.config') ($exe+'.config') -Force
$f=Get-Content $FixtureJson -Raw | ConvertFrom-Json
& $exe $f.directInviteGroupId $f.projectId $f.directRecruitmentId
if($LASTEXITCODE -ne 0){throw 'Staffing freshness failed'}
