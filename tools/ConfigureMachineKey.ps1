param([string]$WebRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'NexusFreelance.Web'))
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -eq 'Core') {
    & "$env:windir/System32/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -WebRoot $WebRoot
    if($LASTEXITCODE -ne 0){throw 'Windows protected machine-key configuration failed.'}
    return
}
Add-Type -AssemblyName System.Configuration
Add-Type -AssemblyName System.Security
$web=(Resolve-Path -LiteralPath $WebRoot).Path
$configPath=Join-Path $web 'Web.config'
$keyPath=Join-Path $web 'App_Data/machineKey.config'
[xml]$config=New-Object Xml.XmlDocument
$config.PreserveWhitespace=$true;$config.Load($configPath)
$node=$config.SelectSingleNode('/configuration/system.web/machineKey')
if($node -and $node.GetAttribute('configSource') -notin @('App_Data\machineKey.config','App_Data/machineKey.config')){throw 'Existing machineKey configuration preserved. Configure a dedicated key explicitly.'}
$provider=New-Object Configuration.DpapiProtectedConfigurationProvider
$settings=New-Object Collections.Specialized.NameValueCollection
$settings.Add('useMachineProtection','true')
$provider.Initialize('DataProtectionConfigurationProvider',$settings)
if(Test-Path -LiteralPath $keyPath) {
    if(!([IO.File]::GetAccessControl($keyPath)).AreAccessRulesProtected){throw 'Existing protected key needs restricted file permissions; it was not rotated.'}
    [xml]$stored=Get-Content -LiteralPath $keyPath -Raw
    if($stored.DocumentElement.GetAttribute('configProtectionProvider') -ne 'DataProtectionConfigurationProvider'){throw 'Existing key file is not in the expected protected format; it was not overwritten.'}
    $cipher=$stored.GetElementsByTagName('EncryptedData')
    if($cipher.Count -ne 1){throw 'Invalid protected key file; it was not overwritten.'}
    try {$plain=$provider.Decrypt($cipher[0])}catch{throw 'Key cannot be decrypted on this Windows machine. Restore its matching configuration; do not silently rotate keys.'}
    if($plain.GetAttribute('validationKey') -notmatch '^[0-9A-F]{128}$' -or $plain.GetAttribute('decryptionKey') -notmatch '^[0-9A-F]{64}$' -or $plain.GetAttribute('validation') -ne 'HMACSHA256' -or $plain.GetAttribute('decryption') -ne 'AES'){throw 'Protected key format is invalid.'}
} else {
    New-Item -ItemType Directory -Path (Split-Path -Parent $keyPath) -Force | Out-Null
    $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $validation=New-Object byte[] 64;$decryption=New-Object byte[] 32
        $rng.GetBytes($validation);$rng.GetBytes($decryption)
        [xml]$plain=('<machineKey validationKey="'+[BitConverter]::ToString($validation).Replace('-','')+'" decryptionKey="'+[BitConverter]::ToString($decryption).Replace('-','')+'" validation="HMACSHA256" decryption="AES" compatibilityMode="Framework45" />')
        $cipher=$provider.Encrypt($plain.DocumentElement)
        [xml]$protected='<machineKey configProtectionProvider="DataProtectionConfigurationProvider" />'
        $null=$protected.DocumentElement.AppendChild($protected.ImportNode($cipher,$true))
        $stream=[IO.File]::Open($keyPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try {$bytes=(New-Object Text.UTF8Encoding($false)).GetBytes($protected.OuterXml);$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
        $acl=New-Object Security.AccessControl.FileSecurity
        $acl.SetAccessRuleProtection($true,$false)
        $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','Allow')))
        [IO.File]::SetAccessControl($keyPath,$acl)
    } finally {$rng.Dispose();if($validation){[Array]::Clear($validation,0,$validation.Length)};if($decryption){[Array]::Clear($decryption,0,$decryption.Length)}}
}
if(!$node) {
    $node=$config.CreateElement('machineKey')
    $null=$config.SelectSingleNode('/configuration/system.web').AppendChild($node)
}
if($node.GetAttribute('configSource') -ne 'App_Data\machineKey.config') {
    $node.SetAttribute('configSource','App_Data\machineKey.config')
    $config.Save($configPath)
}
Write-Output 'MACHINE_KEY_READY_PROTECTED; unique per installation; existing key retained; no key value printed.'
