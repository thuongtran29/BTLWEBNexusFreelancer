$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root 'NexusFreelance.Web\bin'
$compiler = Join-Path $env:windir 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$framework = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$output = Join-Path $bin 'TestAuthentication.exe'
& $compiler /nologo /target:exe "/out:$output" "/reference:$(Join-Path $bin 'NexusFreelance.Web.dll')" "/reference:$(Join-Path $framework 'System.ComponentModel.DataAnnotations.dll')" (Join-Path $PSScriptRoot 'TestAuthentication.cs')
if ($LASTEXITCODE -ne 0) { throw 'Authentication harness compilation failed.' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Authentication tests failed.' }
