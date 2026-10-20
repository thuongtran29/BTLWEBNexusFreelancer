$ErrorActionPreference='Stop'
$bin=Join-Path (Split-Path -Parent $PSScriptRoot) 'NexusFreelance.Web/bin'
$compiler=Join-Path $env:windir 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$output=Join-Path $bin 'TestBackNavigation.exe'
& $compiler /nologo /target:exe "/out:$output" "/reference:$bin/NexusFreelance.Web.dll" (Join-Path $PSScriptRoot 'TestBackNavigation.cs')
if($LASTEXITCODE -ne 0){throw 'Back navigation harness compilation failed.'}
& $output
if($LASTEXITCODE -ne 0){throw 'Back navigation checks failed.'}
