param([Parameter(Mandatory=$true)][string]$AuditWebRoot)
$ErrorActionPreference='Stop';$web=[IO.Path]::GetFullPath($AuditWebRoot);$config=Join-Path $web 'Web.config';if([IO.File]::ReadAllText($config) -notmatch 'Initial Catalog=NexusFreelanceAudit20261019'){throw 'Audit DB only'}
$bin=Join-Path $web 'bin';$exe=Join-Path $bin 'TestGeminiPersistence.exe';Copy-Item $config ($exe+'.config') -Force
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe';$vs=& $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath;$csc=Join-Path $vs 'MSBuild/Current/Bin/Roslyn/csc.exe'
& $csc /nologo /codepage:65001 /target:exe /main:TestGeminiPersistence "/out:$exe" "/reference:$bin/NexusFreelance.Web.dll" "/reference:$bin/EntityFramework.dll" /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.Data.dll /reference:System.ComponentModel.DataAnnotations.dll (Join-Path $PSScriptRoot 'TestGeminiPersistence.cs') (Join-Path $PSScriptRoot 'TestGeminiProvider.cs')
if($LASTEXITCODE -ne 0){throw 'Persistence test compile failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Gemini persistence test failed'}
