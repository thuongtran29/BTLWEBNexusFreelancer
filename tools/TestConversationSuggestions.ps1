# Compile the owned sources into an isolated test executable; no web build, deployment or DB writes.
param([string]$ReferenceWebRoot = (Join-Path $PSScriptRoot '../NexusFreelance.Web'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ReferenceWebRoot)
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../NexusFreelance.Web'))
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('NexusConversationTests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
Get-ChildItem -LiteralPath (Join-Path $root 'bin') -Filter '*.dll' | Copy-Item -Destination $scratch
$output = Join-Path $scratch 'TestConversationSuggestions.exe'
$framework = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'
$refs = @('System.dll','System.Core.dll','System.Data.dll','System.Web.dll','System.Web.Extensions.dll','System.ComponentModel.DataAnnotations.dll','Microsoft.CSharp.dll')
$arguments = @('/nologo','/noconfig','/target:exe','/codepage:65001',"/out:$output", "/reference:$scratch/NexusFreelance.Web.dll", "/reference:$scratch/EntityFramework.dll", "/reference:$scratch/System.Web.Mvc.dll")
$arguments += @("/reference:$scratch/System.Web.Razor.dll", "/reference:$scratch/System.Web.WebPages.dll", "/reference:$scratch/System.Web.WebPages.Razor.dll")
foreach ($ref in $refs) { $arguments += "/reference:$(Join-Path $framework $ref)" }
$arguments += @((Join-Path $source 'Services/ConversationService.cs'), (Join-Path $source 'Controllers/MessagesController.cs'), (Join-Path $PSScriptRoot 'TestConversationSuggestions.cs'))
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$compiler = & $vswhere -products '*' -property installationPath | ForEach-Object { Join-Path $_ 'MSBuild/Current/Bin/Roslyn/csc.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$compiler) { throw 'A Visual Studio Roslyn compiler is required for this source harness.' }
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Messaging harness compile failed.' }
# Synthetic configuration used only for EF SQL translation. The harness never opens a DB connection.
@'
<configuration><connectionStrings><add name="NexusDb" connectionString="Data Source=(local);Initial Catalog=ConversationPolicyTests;Integrated Security=True" providerName="System.Data.SqlClient" /></connectionStrings><runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1"><dependentAssembly><assemblyIdentity name="System.Web.Mvc" publicKeyToken="31bf3856ad364e35" culture="neutral"/><bindingRedirect oldVersion="0.0.0.0-5.2.9.0" newVersion="5.2.9.0"/></dependentAssembly></assemblyBinding></runtime></configuration>
'@ | Set-Content -LiteralPath ($output + '.config') -Encoding UTF8
& $output (Join-Path $source 'Views/Messages/Index.cshtml') (Join-Path $source 'Views/Messages/Thread.cshtml')
if ($LASTEXITCODE -ne 0) { throw 'Messaging tests failed.' }
Write-Output "Isolated harness: $output"
