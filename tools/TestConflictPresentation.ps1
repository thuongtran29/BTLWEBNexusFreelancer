$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root 'NexusFreelance.Web\bin'
$compiler = Join-Path $env:windir 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$output = Join-Path $bin 'TestConflictPresentation.exe'
& $compiler /nologo /target:exe "/out:$output" "/reference:$(Join-Path $bin 'NexusFreelance.Web.dll')" "/reference:$(Join-Path $bin 'System.Web.Mvc.dll')" "/reference:$(Join-Path $bin 'EntityFramework.dll')" (Join-Path $PSScriptRoot 'TestConflictPresentation.cs')
if ($LASTEXITCODE -ne 0) { throw 'Conflict presentation harness compilation failed.' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Conflict presentation tests failed.' }

