$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$temp=Join-Path ([IO.Path]::GetTempPath()) ('NexusExternalSettings-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temp)
try {
    Get-ChildItem -LiteralPath (Join-Path $root 'NexusFreelance.Web/bin') -Filter '*.dll' | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $temp}
    $exe=Join-Path $temp 'TestExternalSettings.exe';$store=Join-Path $temp 'synthetic-user-store.dat'
    [IO.File]::WriteAllText(($exe+'.config'),('<configuration><appSettings><add key="NexusIntegrationStore" value="'+[Security.SecurityElement]::Escape($store)+'" /></appSettings></configuration>'))
    $csc=Join-Path $env:windir 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
    & $csc /nologo /target:exe /codepage:65001 "/out:$exe" "/reference:$temp/NexusFreelance.Web.dll" /reference:System.Configuration.dll /reference:System.Security.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'TestExternalSettings.cs')
    if($LASTEXITCODE -ne 0){throw 'External settings test compilation failed.'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'External settings tests failed.'}
} finally {
    $absolute=[IO.Path]::GetFullPath($temp);$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    if([IO.Path]::GetDirectoryName($absolute) -cne $parent -or [IO.Path]::GetFileName($absolute) -notmatch '^NexusExternalSettings-[0-9a-f]{32}$' -or ((Get-Item -LiteralPath $absolute).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Unsafe settings fixture cleanup path'}
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
