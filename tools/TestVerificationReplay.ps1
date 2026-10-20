$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$bin=Join-Path $root 'NexusFreelance.Web/bin'
$compiler=Join-Path $env:windir 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$output=Join-Path $bin 'TestVerificationReplay.exe'
& $compiler /nologo /target:exe /codepage:65001 "/out:$output" "/reference:$bin/NexusFreelance.Web.dll" "/reference:$bin/EntityFramework.dll" /reference:System.Data.dll /reference:System.ComponentModel.DataAnnotations.dll (Join-Path $PSScriptRoot 'TestVerificationReplay.cs')
if($LASTEXITCODE -ne 0){throw 'Verification test compilation failed.'}
Copy-Item -LiteralPath (Join-Path $root 'NexusFreelance.Web/Web.config') -Destination ($output+'.config') -Force
& $output
if($LASTEXITCODE -ne 0){throw 'Verification replay checks failed.'}
