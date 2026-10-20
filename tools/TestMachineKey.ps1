$ErrorActionPreference='Stop'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('NexusMachineKey-'+[Guid]::NewGuid().ToString('N'))
$configure=Join-Path $PSScriptRoot 'ConfigureMachineKey.ps1'
$count=0
function Check([bool]$ok,[string]$label){if(!$ok){throw $label};$script:count++;Write-Output ('PASS '+$label)}
try {
    New-Item -ItemType Directory -Path $temp | Out-Null
    $config='<configuration><system.web><machineKey configSource="App_Data\machineKey.config" /></system.web></configuration>'
    [IO.File]::WriteAllText((Join-Path $temp 'Web.config'),$config)
    & $configure -WebRoot $temp
    $key=Join-Path $temp 'App_Data/machineKey.config'
    $bytes=[IO.File]::ReadAllBytes($key);$hash=(Get-FileHash -LiteralPath $key).Hash
    [xml]$stored=[IO.File]::ReadAllText($key)
    Check ($stored.machineKey.configProtectionProvider -eq 'DataProtectionConfigurationProvider') 'Protected configuration provider is DPAPI'
    Check (![IO.File]::ReadAllText($key).Contains('validationKey=')) 'No plaintext key attribute on disk'
    Check ((Get-Acl -LiteralPath $key).AreAccessRulesProtected) 'Key ACL does not inherit broad permissions'
    & $configure -WebRoot $temp
    Check ((Get-FileHash -LiteralPath $key).Hash -eq $hash) 'Repeated configuration preserves the same encrypted key'
    Check ([IO.File]::ReadAllText((Join-Path $temp 'Web.config')) -eq $config) 'Portable configSource template stays unchanged'
    [IO.File]::WriteAllText($key,'<machineKey validationKey="synthetic" />')
    $failed=$false
    try {& $configure -WebRoot $temp}catch{$failed=$true}
    Check $failed 'Unexpected existing key is rejected, not silently rotated'
    Check ([IO.File]::ReadAllText($key) -eq '<machineKey validationKey="synthetic" />') 'Rejected key file is not overwritten'
    [IO.File]::WriteAllBytes($key,$bytes)
    & $configure -WebRoot $temp
    Check ((Get-FileHash -LiteralPath $key).Hash -eq $hash) 'Restored protected key is accepted without rewriting'
    Write-Output ('MACHINE_KEY_CHECKS='+$count)
}finally{
    $safe=[IO.Path]::GetFullPath($temp)
    if((Split-Path -Parent $safe) -eq [IO.Path]::GetTempPath().TrimEnd('\') -and (Split-Path -Leaf $safe).StartsWith('NexusMachineKey-')){Remove-Item -LiteralPath $safe -Recurse -Force}
}
