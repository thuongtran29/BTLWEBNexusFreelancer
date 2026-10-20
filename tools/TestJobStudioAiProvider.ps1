# Build the tiny .NET Framework harness against the actual MVC5 project output.
# No network connection, OpenAI key, SQL database or IIS process is required.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root 'NexusFreelance.Web\bin'
$assembly = Join-Path $bin 'NexusFreelance.Web.dll'
$compiler = Join-Path $env:windir 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$framework = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$output = Join-Path $bin 'TestJobStudioAiProvider.exe'

if (!(Test-Path $assembly)) { throw 'Run BuildFromSource.ps1 before this test.' }
if (!(Test-Path $compiler)) { throw 'The .NET Framework C# compiler is required.' }
if (!(Test-Path $framework)) { throw 'The .NET Framework 4.8 reference assemblies are required.' }

& $compiler /nologo /target:exe "/out:$output" "/reference:$assembly" `
    "/reference:$(Join-Path $framework 'System.Net.Http.dll')" `
    (Join-Path $PSScriptRoot 'TestJobStudioAiProvider.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fake-provider test compilation failed.' }

& $output
if ($LASTEXITCODE -ne 0) { throw 'Fake-provider tests failed.' }
