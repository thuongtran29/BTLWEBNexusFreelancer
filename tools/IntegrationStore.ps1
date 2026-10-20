# Shared by setup and diagnostic tools. Never prints credential values.
function Get-NexusIntegrationStorePath([string]$SolutionRoot, [switch]$ForWrite) {
    $legacy = Join-Path $SolutionRoot '_local/integrations.dat'
    # Setup must save where the web process can see it under F5 as well as a
    # terminal launch. Read-only diagnostics may still inspect a shared store.
    if ($ForWrite) { return [IO.Path]::GetFullPath($legacy) }
    if (Test-Path -LiteralPath $legacy) { return [IO.Path]::GetFullPath($legacy) }
    $config = Join-Path $SolutionRoot 'NexusFreelance.Web/Web.config'
    if (Test-Path -LiteralPath $config) {
        [xml]$xml = [IO.File]::ReadAllText($config)
        $setting = @($xml.configuration.appSettings.add | Where-Object { $_.key -ceq 'NexusIntegrationStore' })
        if ($setting.Count -eq 1 -and $setting[0].value) {
            $path = [Environment]::ExpandEnvironmentVariables([string]$setting[0].value)
            if (![IO.Path]::IsPathRooted($path)) { throw 'Integration store must be an absolute path.' }
            return [IO.Path]::GetFullPath($path)
        }
    }
    return [IO.Path]::GetFullPath($legacy)
}
function Read-NexusIntegrationStore([string]$Path) {
    Add-Type -AssemblyName System.Security
    $values = @{}
    if (!(Test-Path -LiteralPath $Path)) { return $values }
    if ((Get-Item -LiteralPath $Path).Length -gt 65536) { throw 'Protected settings are too large.' }
    $bytes = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($Path),
        [Text.Encoding]::UTF8.GetBytes('NexusFreelance.integrations.v1'), [Security.Cryptography.DataProtectionScope]::CurrentUser)
    try {
        $stored = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
        if ($stored -isnot [pscustomobject]) { throw 'Invalid protected settings.' }
        foreach ($p in $stored.PSObject.Properties) {
            if ($null -ne $p.Value -and $p.Value -isnot [string]) { throw 'Invalid protected setting type.' }
            $values[$p.Name] = [string]$p.Value
        }
    } finally { [Array]::Clear($bytes, 0, $bytes.Length) }
    return $values
}
function Get-NexusStoreHash([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return '' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($Path))).Replace('-','') }
    finally { $sha.Dispose() }
}
function Write-NexusIntegrationStore([string]$Path, [hashtable]$Settings, [string]$ExpectedHash) {
    Add-Type -AssemblyName System.Security
    $directory = Split-Path -Parent $Path
    foreach ($entry in @($directory, $Path)) {
        if ((Test-Path -LiteralPath $entry) -and ((Get-Item -LiteralPath $entry -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Refusing redirected settings directory or file.'
        }
    }
    [void][IO.Directory]::CreateDirectory($directory)
    $bytes = [Text.Encoding]::UTF8.GetBytes(($Settings | ConvertTo-Json -Compress))
    try { $encrypted = [Security.Cryptography.ProtectedData]::Protect($bytes,
        [Text.Encoding]::UTF8.GetBytes('NexusFreelance.integrations.v1'), [Security.Cryptography.DataProtectionScope]::CurrentUser) }
    finally { [Array]::Clear($bytes, 0, $bytes.Length) }
    if ($encrypted.Length -gt 65536) { throw 'Protected settings are too large.' }
    $temporary = Join-Path $directory ('settings-' + [guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllBytes($temporary, $encrypted)
        $acl = New-Object Security.AccessControl.FileSecurity
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,
            [Security.Principal.SecurityIdentifier]::new('S-1-5-18'), [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))) {
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'Allow'))
        }
        if ($PSVersionTable.PSVersion.Major -ge 6) { [IO.FileSystemAclExtensions]::SetAccessControl([IO.FileInfo]::new($temporary), $acl) }
        else { [IO.File]::SetAccessControl($temporary, $acl) }
        if ((Get-NexusStoreHash $Path) -cne $ExpectedHash) { throw 'Settings changed while the form was open. Reopen it before saving.' }
        if (Test-Path -LiteralPath $Path) {
            if ($PSVersionTable.PSVersion.Major -ge 6) { [IO.FileSystemAclExtensions]::SetAccessControl([IO.FileInfo]::new($Path), $acl) }
            else { [IO.File]::SetAccessControl($Path, $acl) }
            [IO.File]::Replace($temporary, $Path, [NullString]::Value)
        }
        else { [IO.File]::Move($temporary, $Path) }
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}
