$ErrorActionPreference='Stop'
$bin=Join-Path (Split-Path -Parent $PSScriptRoot) 'NexusFreelance.Web/bin'
$compiler=Join-Path $env:windir 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$output=Join-Path $bin 'TestAccountIntegrations.exe'
& $compiler /nologo /target:exe /codepage:65001 "/out:$output" "/reference:$bin/NexusFreelance.Web.dll" "/reference:$bin/System.Web.Mvc.dll" /reference:System.Web.dll (Join-Path $PSScriptRoot 'TestAccountIntegrations.cs')
if($LASTEXITCODE -ne 0){throw 'Integration harness compilation failed.'}
& $output
if($LASTEXITCODE -ne 0){throw 'Integration checks failed.'}
