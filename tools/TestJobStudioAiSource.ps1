# Compile only an isolated test harness from current AI sources. Never build/deploy the web app.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$web = Join-Path $root 'NexusFreelance.Web'
$bin = Join-Path $web 'bin'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vsRoot = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$compiler = Join-Path $vsRoot 'MSBuild/Current/Bin/Roslyn/csc.exe'
$runtime = Join-Path $PSScriptRoot ('_runtime/JobStudioAi-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $runtime)
foreach ($name in @('NexusFreelance.Web.dll', 'EntityFramework.dll', 'EntityFramework.SqlServer.dll', 'System.Web.Mvc.dll', 'System.Web.Razor.dll')) {
    Copy-Item -LiteralPath (Join-Path $bin $name) -Destination $runtime
}
$output = Join-Path $runtime 'TestJobStudioAiSource.exe'
$arguments = @('/nologo', '/target:exe', '/codepage:65001', '/nowarn:0436', "/out:$output",
    "/reference:$bin/NexusFreelance.Web.dll", "/reference:$bin/EntityFramework.dll",
    "/reference:$bin/System.Web.Razor.dll",
    '/reference:System.Net.Http.dll', '/reference:System.Web.dll', '/reference:System.Web.Extensions.dll',
    '/reference:System.ComponentModel.DataAnnotations.dll', '/reference:System.Data.dll',
    (Join-Path $web 'Services/JobPrompt.cs'), (Join-Path $web 'Services/JobStudioService.cs'),
    (Join-Path $web 'Services/OpenAiJobDraftGenerator.cs'), (Join-Path $web 'Services/ChatCompletionJobDraftGenerator.cs'), (Join-Path $web 'Services/GeminiJobDraftGenerator.cs'), (Join-Path $PSScriptRoot 'TestJobStudioAiSource.cs'))
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'AI source test harness compilation failed.' }
& $output (Join-Path $web 'Views/JobStudio')
if ($LASTEXITCODE -ne 0) { throw 'AI source tests failed.' }
