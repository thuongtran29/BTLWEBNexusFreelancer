# Check C# and Razor against the project's restored MVC5 / EF6 dependencies.
# Run BuildFromSource.ps1 before this check. SQL and HTTP checks are separate.
$ErrorActionPreference = 'Stop'
$solution = Split-Path -Parent $PSScriptRoot
$source = Join-Path $solution 'NexusFreelance.Web'
$scratch = Join-Path $PSScriptRoot '_offline'
$stage = Join-Path $scratch 'stage'
$target = Join-Path $scratch 'razor-compiled'
$temp = Join-Path $scratch 'aspnet-temp'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path $vswhere)) { throw 'Visual Studio vswhere.exe was not found.' }
$vsRoot = & $vswhere -latest -products Microsoft.VisualStudio.Product.Community Microsoft.VisualStudio.Product.Professional Microsoft.VisualStudio.Product.Enterprise -property installationPath
if (!$vsRoot) { throw 'Visual Studio installation was not found.' }
$vsIde = Join-Path $vsRoot 'Common7\IDE'
$msbuild = Join-Path $vsRoot 'MSBuild\Current\Bin\MSBuild.exe'
$compiler = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\aspnet_compiler.exe'
$appBin = Join-Path $source 'bin'
$required = @('EntityFramework.dll', 'EntityFramework.SqlServer.dll',
    'System.Web.Mvc.dll', 'System.Web.Razor.dll', 'System.Web.WebPages.dll',
    'System.Web.WebPages.Razor.dll', 'System.Web.WebPages.Deployment.dll',
    'System.Web.Helpers.dll', 'Microsoft.Web.Infrastructure.dll')
foreach ($name in $required) {
    if (!(Test-Path (Join-Path $appBin $name))) {
        throw "Missing $name. Run .\tools\BuildFromSource.ps1 first."
    }
}

& $msbuild (Join-Path $PSScriptRoot 'OfflineCompile.proj') /t:Rebuild /nologo /v:minimal "/p:ProjectBin=$appBin"
if ($LASTEXITCODE -ne 0) { throw 'Offline C# compilation failed.' }

$scratchRoot = [System.IO.Path]::GetFullPath($scratch)
foreach ($generatedPath in @($stage, $target)) {
    $resolvedGenerated = [System.IO.Path]::GetFullPath($generatedPath)
    if ([System.IO.Path]::GetDirectoryName($resolvedGenerated) -ne $scratchRoot) { throw 'Generated path escaped the offline workspace.' }
    if (Test-Path -LiteralPath $resolvedGenerated) {
        if ((Get-Item -LiteralPath $resolvedGenerated).Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw 'Generated path must not be a filesystem link.' }
        Remove-Item -LiteralPath $resolvedGenerated -Recurse -Force
    }
}
New-Item -ItemType Directory -Path $stage, (Join-Path $stage 'bin'), $temp, $target -Force | Out-Null
Get-ChildItem -LiteralPath $source -Force | Where-Object { $_.Name -notin @('bin','obj','App_Data') } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $stage -Recurse -Force
}

# Stage uses the exact restored dependencies selected by the solution build.
$configPath = Join-Path $stage 'Web.config'
$config = Get-Content -LiteralPath $configPath -Raw
$config = $config.Replace('targetFramework="4.8" />', 'targetFramework="4.8" tempDirectory="' + $temp + '" />')
Set-Content -LiteralPath $configPath -Value $config -Encoding UTF8

Copy-Item -LiteralPath (Join-Path $scratch 'bin\NexusFreelance.OfflineCheck.dll') -Destination (Join-Path $stage 'bin') -Force
& (Join-Path $PSScriptRoot 'ConfigureMachineKey.ps1') -WebRoot $stage
foreach ($dll in Get-ChildItem -LiteralPath $appBin -Filter '*.dll' |
    Where-Object Name -ne 'NexusFreelance.Web.dll') {
    Copy-Item -LiteralPath $dll.FullName -Destination (Join-Path $stage 'bin') -Force
}

& $compiler -v / -p $stage -f $target
if ($LASTEXITCODE -ne 0) { throw 'Offline Razor compilation failed.' }
Write-Host 'Offline C# and Razor checks passed. Verify solution build, NuGet restore and SQL runtime separately.'
