param(
    [ValidateSet('all','codec','api-vectors','query','query-promotion','refund-full','refund-partial','tamper','cancellation','classification')]
    [string]$Case = 'all'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root 'NexusFreelance.Web\bin'
$compiler = Join-Path $env:windir 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$framework = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
if (!(Test-Path -LiteralPath (Join-Path $bin 'NexusFreelance.Web.dll'))) {
    throw 'Build NexusFreelance.Web first; this harness never builds or changes the application.'
}
if (!(Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler is missing.' }
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')
$prefix = 'NexusVnPayTests-'
$temp = Join-Path $tempRoot ($prefix + [Guid]::NewGuid().ToString('N'))
$created = $false
try {
    New-Item -ItemType Directory -Path $temp -ErrorAction Stop | Out-Null
    $created = $true
    # Snapshot runtime dependencies away from shared bin; no app config/credentials.
    Get-ChildItem -LiteralPath $bin -Filter '*.dll' -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $temp
    }
    $output = Join-Path $temp 'TestVnPayGateway.exe'
    $references = @('System.dll','System.Core.dll','System.Net.Http.dll','System.Web.dll','System.Web.Extensions.dll','System.ComponentModel.DataAnnotations.dll')
    $arguments = @('/nologo','/noconfig','/target:exe','/langversion:5','/codepage:65001',"/out:$output", "/reference:$(Join-Path $temp 'NexusFreelance.Web.dll')")
    foreach ($reference in $references) {
        $path = Join-Path $framework $reference
        if (!(Test-Path -LiteralPath $path)) { throw "Missing .NET Framework reference: $reference" }
        $arguments += "/reference:$path"
    }
    $arguments += Join-Path $PSScriptRoot 'TestVnPayGateway.cs'
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw 'VNPay harness compilation failed; use a build containing the public gateway and refund outcome APIs.' }
    & $output $Case
    if ($LASTEXITCODE -ne 0) { throw "VNPay harness failed (exit $LASTEXITCODE)." }
}
finally {
    if ($created -and (Test-Path -LiteralPath $temp)) {
        $resolved = (Resolve-Path -LiteralPath $temp).ProviderPath
        $absolute = [IO.Path]::GetFullPath($resolved)
        $parent = [IO.Path]::GetDirectoryName($absolute).TrimEnd('\','/')
        $leaf = [IO.Path]::GetFileName($absolute)
        if (!$parent.Equals($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or
            $leaf -notmatch '^NexusVnPayTests-[0-9a-f]{32}$' -or
            !$absolute.Equals([IO.Path]::GetFullPath($temp),[StringComparison]::OrdinalIgnoreCase) -or
            ((Get-Item -LiteralPath $absolute).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Refusing cleanup outside the uniquely created VNPay harness temporary directory.'
        }
        Remove-Item -LiteralPath $absolute -Recurse -Force
    }
}
