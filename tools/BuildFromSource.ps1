<#
.SYNOPSIS
Restores MVC5 / EF6 NuGet PackageReferences and builds the clean source tree.
.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\tools\BuildFromSource.ps1
.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\tools\BuildFromSource.ps1 -Configuration Release -Offline
#>
param(
    [ValidateSet('Debug', 'Release', 'Both')]
    [string]$Configuration = 'Both',
    [switch]$Offline
)

$ErrorActionPreference = 'Stop'
$solution = Join-Path (Split-Path -Parent $PSScriptRoot) 'NexusFreelance.sln'
$installer = [Environment]::GetFolderPath('ProgramFilesX86')
$vswhere = Join-Path $installer 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path $vswhere)) { throw 'Visual Studio Installer / vswhere.exe is required.' }
$vsRoot = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if (!$vsRoot) { throw 'MSBuild is not installed. Install Visual Studio with the ASP.NET workload.' }
$msbuild = Join-Path $vsRoot 'MSBuild\Current\Bin\MSBuild.exe'
if (!(Test-Path $msbuild)) { throw "MSBuild missing at $msbuild" }

$configurations = if ($Configuration -eq 'Both') { @('Debug', 'Release') } else { @($Configuration) }
foreach ($configurationName in $configurations) {
    $args = @(
        $solution, '/restore', '/t:Build', "/p:Configuration=$configurationName",
        '/m:1', '/nologo', '/v:minimal'
    )
    if ($Offline) {
        # A completely offline machine must already have the required packages cached.
        $args += '/p:RestoreIgnoreFailedSources=true'
        $args += '/p:NuGetAudit=false'
    }
    & $msbuild @args
    if ($LASTEXITCODE -ne 0) {
        throw "Build $configurationName failed (MSBuild exit code $LASTEXITCODE)."
    }
    Write-Host "Source restore + $configurationName build PASSED."
}
