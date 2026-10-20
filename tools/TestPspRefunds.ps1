param([int]$AdminId,[int]$PaymentOne,[int]$PaymentTwo,[int]$PaymentThree,[int]$CapturePaymentId,[int]$ClientId,[int]$ProjectId)
$ErrorActionPreference='Stop'
$PaymentIds=@($PaymentOne,$PaymentTwo,$PaymentThree)
if($CapturePaymentId -gt 0) {if($ClientId -le 0 -or $ProjectId -le 0){throw 'Capture race requires client and project fixture IDs.'}}
elseif($AdminId -le 0 -or @($PaymentIds|Where-Object{$_ -le 0}).Count -gt 0){throw 'Refund test requires admin and three payment fixture IDs.'}
$root=Split-Path -Parent $PSScriptRoot
$bin=Join-Path $root 'NexusFreelance.Web/bin'
$scratch=Join-Path $PSScriptRoot ('_runtime/PspRefundHarness-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
Get-ChildItem -LiteralPath $bin -File -Filter '*.dll' | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $scratch}
$exe=Join-Path $scratch 'TestPspRefunds.exe'
$framework=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'
$arguments=@('/nologo','/noconfig','/target:exe','/codepage:65001',"/out:$exe",("/reference:"+(Join-Path $scratch 'NexusFreelance.Web.dll')),("/reference:"+(Join-Path $scratch 'EntityFramework.dll')))
foreach($dll in @('System.dll','System.Core.dll','System.Data.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.ComponentModel.DataAnnotations.dll')) {$arguments+=('/reference:'+(Join-Path $framework $dll))}
$arguments+=Join-Path $PSScriptRoot 'TestPspRefunds.cs'
& (Join-Path $env:windir 'Microsoft.NET/Framework/v4.0.30319/csc.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'PSP SQL harness compilation failed.'}
[xml]$web=Get-Content (Join-Path $root 'NexusFreelance.Web/Web.config') -Raw
$config=New-Object System.Xml.XmlDocument
$null=$config.AppendChild($config.CreateElement('configuration'))
foreach($section in @('configSections','connectionStrings','runtime','entityFramework')) {$null=$config.DocumentElement.AppendChild($config.ImportNode($web.configuration.SelectSingleNode($section),$true))}
$config.Save($exe+'.config')
if($CapturePaymentId -gt 0){& $exe '--capture-dispute-race' $CapturePaymentId $ClientId $ProjectId}
else{& $exe '--disposable-local-fixtures' $AdminId $PaymentIds[0] $PaymentIds[1] $PaymentIds[2]}
if($LASTEXITCODE -ne 0){throw 'PSP SQL integration failed.'}
