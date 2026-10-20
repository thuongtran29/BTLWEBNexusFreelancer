param([Parameter(Mandatory=$true)][string]$AuditWebRoot, [switch]$PolicyOnly, [switch]$RolesOnly)
$ErrorActionPreference='Stop'
$resolved=[IO.Path]::GetFullPath($AuditWebRoot)
$config=Join-Path $resolved 'Web.config'
if(!(Test-Path -LiteralPath $config) -or [IO.File]::ReadAllText($config) -notmatch 'Initial Catalog=NexusFreelanceAudit[0-9]{8}') { throw 'Requires an isolated audit web root/DB, never the live site.' }
$bin=Join-Path $resolved 'bin'
$compiler=Join-Path $env:windir 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$output=Join-Path $bin 'TestPlatformExtensions.exe'
$refs=@('System.dll','System.Core.dll','System.Data.dll','System.Web.dll','System.Web.Extensions.dll','System.Net.Http.dll','System.ComponentModel.DataAnnotations.dll')
$framework=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$arguments=@('/nologo','/noconfig','/target:exe','/codepage:65001',"/out:$output","/reference:$bin\NexusFreelance.Web.dll","/reference:$bin\EntityFramework.dll")
foreach($ref in $refs) { $arguments+="/reference:$(Join-Path $framework $ref)" }
$arguments+=Join-Path $PSScriptRoot 'TestPlatformExtensions.cs'
& $compiler @arguments
if($LASTEXITCODE -ne 0) { throw 'Platform harness compile failed.' }
Copy-Item -LiteralPath $config -Destination ($output+'.config') -Force
if($RolesOnly) { & $output --roles } elseif($PolicyOnly) { & $output --policy } else { & $output }
if($LASTEXITCODE -ne 0) { throw 'Platform extension tests failed.' }
