param([string]$SourceName='TestKnowledgePositions')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$bin=Join-Path $root 'NexusFreelance.Web/bin'
$exe=Join-Path $bin ($SourceName+'.exe')
$source=Join-Path $PSScriptRoot ($SourceName+'.cs')
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vsRoot=& $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$csc=Join-Path $vsRoot 'MSBuild/Current/Bin/Roslyn/csc.exe'
& $csc /nologo /codepage:65001 /target:exe "/out:$exe" "/reference:$bin/NexusFreelance.Web.dll" "/reference:$bin/EntityFramework.dll" /reference:System.ComponentModel.DataAnnotations.dll /reference:System.Data.dll $source
if($LASTEXITCODE -ne 0){throw 'Harness compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Knowledge/position policy failed'}
