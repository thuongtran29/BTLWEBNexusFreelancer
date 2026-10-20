$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'NexusFreelance.Web/Services'
$compiler = Join-Path $env:windir 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$output = Join-Path ([IO.Path]::GetTempPath()) ('NexusAdminActivity-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    # Compile only pure policy sources into an isolated test harness; never rebuild the web app.
    & $compiler /nologo /target:exe "/out:$output" (Join-Path $source 'ActivityAccessPolicy.cs') (Join-Path $source 'ActivityFilters.cs') (Join-Path $PSScriptRoot 'TestAdminActivity.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Admin activity policy test compilation failed.' }
    & $output
    if ($LASTEXITCODE -ne 0) { throw 'Admin activity policy tests failed.' }
}
finally { if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output } }
