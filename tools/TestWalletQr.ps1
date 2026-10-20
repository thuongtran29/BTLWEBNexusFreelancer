$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'NexusFreelance.Web'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('NexusWalletQr-' + [Guid]::NewGuid().ToString('N'))
$compiler = Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Roslyn C# compiler required for the source-only test harness.' }
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    # Compile wallet sources into an isolated harness, never build/deploy or write shared bin.
    Get-ChildItem -LiteralPath (Join-Path $app 'bin') -Filter '*.dll' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $temp }
    $exe = Join-Path $temp 'TestWalletQr.exe'
    $arguments = @('/nologo','/target:exe','/codepage:65001','/nowarn:0436',"/out:$exe")
    foreach ($name in @('NexusFreelance.Web.dll','EntityFramework.dll','System.Web.Mvc.dll','System.Web.Razor.dll','System.Web.WebPages.dll','System.Web.WebPages.Razor.dll')) { $arguments += "/reference:$(Join-Path $temp $name)" }
    foreach ($name in @('System.dll','System.Core.dll','System.Data.dll','System.Configuration.dll','System.Web.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.ComponentModel.DataAnnotations.dll','Microsoft.CSharp.dll')) { $arguments += "/reference:$name" }
    $arguments += @((Join-Path $PSScriptRoot 'TestWalletQr.cs'), (Join-Path $app 'Services/WalletBankTransferService.cs'), (Join-Path $app 'Services/WalletService.cs'), (Join-Path $app 'Controllers/WalletController.cs'))
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Wallet source harness compilation failed.' }
    # Synthetic isolated LocalDB only; no app configuration or credentials are copied.
    @'
<configuration><connectionStrings><add name="NexusDb" providerName="System.Data.SqlClient" connectionString="Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=NexusWalletQrUnused;Integrated Security=True" /></connectionStrings><runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1"><dependentAssembly><assemblyIdentity name="System.Web.Mvc" publicKeyToken="31bf3856ad364e35" culture="neutral"/><bindingRedirect oldVersion="0.0.0.0-5.2.9.0" newVersion="5.2.9.0"/></dependentAssembly></assemblyBinding></runtime></configuration>
'@ | Set-Content -LiteralPath ($exe + '.config') -Encoding UTF8
    & $exe $root
    if ($LASTEXITCODE -ne 0) { throw 'Wallet QR tests failed.' }
}
finally {
    $resolved = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $temp).ProviderPath)
    if ([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or
        [IO.Path]::GetFileName($resolved) -notmatch '^NexusWalletQr-[0-9a-f]{32}$' -or
        ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unsafe harness cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
