$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot;$bin=Join-Path $root 'NexusFreelance.Web/bin';$runtime=Join-Path $PSScriptRoot ('_runtime/Gemini-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $runtime -Force | Out-Null
Get-ChildItem $bin -Filter '*.dll' | ForEach-Object {Copy-Item $_.FullName $runtime -Force}
$exe=Join-Path $runtime 'TestGeminiProvider.exe';$source=Join-Path $PSScriptRoot 'TestGeminiProvider.cs'
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe';$vs=& $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath;$csc=Join-Path $vs 'MSBuild/Current/Bin/Roslyn/csc.exe'
& $csc /nologo /codepage:65001 /target:exe "/out:$exe" "/reference:$bin/NexusFreelance.Web.dll" /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.ComponentModel.DataAnnotations.dll $source
if($LASTEXITCODE -ne 0){throw 'Gemini harness compile failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Gemini provider tests failed'}
