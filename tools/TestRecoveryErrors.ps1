$ErrorActionPreference='Stop'
$bin=Join-Path (Split-Path -Parent $PSScriptRoot) 'NexusFreelance.Web/bin'
$compiler=Join-Path $env:windir 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$output=Join-Path $bin 'TestRecoveryErrors.exe'
& $compiler /nologo /target:exe /codepage:65001 "/out:$output" "/reference:$bin/NexusFreelance.Web.dll" "/reference:$bin/System.Web.Mvc.dll" "/reference:$bin/EntityFramework.dll" /reference:System.Web.dll (Join-Path $PSScriptRoot 'TestRecoveryErrors.cs')
if($LASTEXITCODE -ne 0){throw 'Recovery error test compilation failed.'}
& $output
if($LASTEXITCODE -ne 0){throw 'Recovery error checks failed.'}
