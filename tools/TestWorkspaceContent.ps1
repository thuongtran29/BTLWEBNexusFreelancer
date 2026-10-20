$ErrorActionPreference='Stop'
$bin=Join-Path (Split-Path -Parent $PSScriptRoot) 'NexusFreelance.Web/bin'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('NexusContent-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $bin 'NexusFreelance.Web.dll') -Destination $temp
    $exe=Join-Path $temp 'TestWorkspaceContent.exe'
    & "$env:windir/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe "/out:$exe" "/reference:$temp/NexusFreelance.Web.dll" /reference:System.IO.Compression.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'TestWorkspaceContent.cs')
    if($LASTEXITCODE -ne 0){throw 'Content test compilation failed.'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Content test failed.'}
} finally {
    $safe=[IO.Path]::GetFullPath($temp)
    if((Split-Path -Parent $safe) -eq [IO.Path]::GetTempPath().TrimEnd('\') -and (Split-Path -Leaf $safe).StartsWith('NexusContent-')){Remove-Item -LiteralPath $safe -Recurse -Force}
}
