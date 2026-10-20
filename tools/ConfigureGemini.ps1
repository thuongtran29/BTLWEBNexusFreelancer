[CmdletBinding(DefaultParameterSetName='Configure')]
param(
    [Parameter(ParameterSetName='Configure')]
    [ValidatePattern('\Agemini-[A-Za-z0-9._-]{1,26}\z')][string]$Model = 'gemini-3.5-flash-lite',
    [Parameter(Mandatory=$true, ParameterSetName='Status')][switch]$Status,
    [Parameter(Mandatory=$true, ParameterSetName='Disable')][switch]$Disable,
    [Parameter(ParameterSetName='Configure')][switch]$Gui
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
if ($Gui) { Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing }
$solution = Split-Path -Parent $PSScriptRoot
$private = Join-Path $solution '_local'
$path = Join-Path $private 'integrations.dat'
$storeHelper = Join-Path $PSScriptRoot 'IntegrationStore.ps1'
if (Test-Path -LiteralPath $storeHelper) {
    . $storeHelper
    $path = Get-NexusIntegrationStorePath -SolutionRoot (Split-Path -Parent $PSScriptRoot)
    $private = Split-Path -Parent $path
}
$entropy = [Text.Encoding]::UTF8.GetBytes('NexusFreelance.integrations.v1')
$settings = @{}
$secret = $null
$pointer = [IntPtr]::Zero
$temporary = $null
function Get-ProtectedStoreHash([string]$filePath) {
    if (![IO.File]::Exists($filePath)) { return $null }
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash([IO.File]::ReadAllBytes($filePath))).Replace('-', '') }
    finally { $algorithm.Dispose() }
}
function Set-AiFileAccess([string]$filePath, [Security.AccessControl.FileSecurity]$access) {
    # Set-Acl can request audit privileges on replacement. Apply only the DACL through .NET.
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        [IO.FileSystemAclExtensions]::SetAccessControl([IO.FileInfo]::new($filePath), $access)
    } else { [IO.File]::SetAccessControl($filePath, $access) }
}
try {
    $before = Get-ProtectedStoreHash $path
    if (Test-Path -LiteralPath $path) {
        try {
            if ((Get-Item -LiteralPath $path).Length -gt 65536) { throw 'Oversize settings' }
            $clear = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($path), $entropy,
                [Security.Cryptography.DataProtectionScope]::CurrentUser)
            try {
                $stored = [Text.Encoding]::UTF8.GetString($clear) | ConvertFrom-Json
                if ($null -eq $stored -or $stored -isnot [pscustomobject]) { throw 'Invalid settings' }
                foreach ($property in $stored.PSObject.Properties) {
                    if ($null -ne $property.Value -and $property.Value -isnot [string]) { throw 'Invalid setting value' }
                    $settings[$property.Name] = [string]$property.Value
                }
            } finally { [Array]::Clear($clear, 0, $clear.Length) }
        } catch {
            throw 'Cannot read encrypted local settings for this Windows account. Existing settings were not changed. Run under the web application account or restore a valid local settings file.'
        }
    }
    $keys = @('NEXUS_AI_ENABLED', 'NEXUS_AI_PROVIDER', 'NEXUS_GEMINI_API_KEY', 'NEXUS_GEMINI_MODEL')
    $overrides = @($keys | Where-Object { ![string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_, 'Process')) })
    if ($Status) {
        $effective = @{}
        foreach ($name in $keys) {
            $value = [Environment]::GetEnvironmentVariable($name, 'Process')
            $effective[$name] = if ([string]::IsNullOrWhiteSpace($value)) { $settings[$name] } else { $value }
        }
        # Presence/configuration only: never print credentials, validate an account or send HTTP.
        [pscustomobject]@{
            LocalSettingsPresent = Test-Path -LiteralPath $path
            LocalAiEnabled = ($settings['NEXUS_AI_ENABLED'] -eq 'true' -and $settings['NEXUS_AI_PROVIDER'] -eq 'Gemini')
            SelectedProvider = if ([string]::IsNullOrWhiteSpace($effective['NEXUS_AI_PROVIDER'])) { 'OpenAI' } else { $effective['NEXUS_AI_PROVIDER'] }
            GeminiModel = $effective['NEXUS_GEMINI_MODEL']
            EffectiveAiConfigured = ($effective['NEXUS_AI_ENABLED'] -eq 'true' -and $effective['NEXUS_AI_PROVIDER'] -eq 'Gemini' -and
                ![string]::IsNullOrWhiteSpace($effective['NEXUS_GEMINI_API_KEY']) -and
                $effective['NEXUS_GEMINI_MODEL'] -cmatch '\A[A-Za-z0-9._:-]{1,33}\z')
            EnvironmentOverrideNames = $overrides -join ', '
            ProviderConnectionTested = $false
        }
        $effective.Clear()
        return
    }

    # Detect updates while the operator enters the key, preserving other integrations.
    if ($Disable) { $settings['NEXUS_AI_ENABLED'] = 'false' }
    else {
        if ($Gui) {
            Add-Type -AssemblyName System.Windows.Forms
            Add-Type -AssemblyName System.Drawing
            $dialog = New-Object System.Windows.Forms.Form
            $dialog.Text = 'NexusFreelance - Gemini secure setup'
            $dialog.ClientSize = New-Object System.Drawing.Size(510,210)
            $dialog.StartPosition = 'CenterScreen'; $dialog.TopMost = $true; $dialog.ShowInTaskbar = $true
            $dialog.Add_Shown({ $this.Activate(); $this.BringToFront() })
            $dialog.FormBorderStyle = 'FixedDialog'; $dialog.MaximizeBox = $false; $dialog.MinimizeBox = $false
            $label = New-Object System.Windows.Forms.Label
            $label.Text = 'Paste your Gemini API key below. It is encrypted on this Windows account.'
            $label.SetBounds(18,18,472,40); $dialog.Controls.Add($label)
            $box = New-Object System.Windows.Forms.TextBox
            $box.UseSystemPasswordChar = $true; $box.MaxLength = 512; $box.SetBounds(18,65,470,28); $dialog.Controls.Add($box)
            $note = New-Object System.Windows.Forms.Label
            $note.Text = 'Model: ' + $Model + '. Free quota depends on your Google project. No API call is made when saving.'
            $note.SetBounds(18,105,470,42); $dialog.Controls.Add($note)
            $save = New-Object System.Windows.Forms.Button
            $save.Text = 'Save encrypted'; $save.SetBounds(270,158,120,32); $save.DialogResult = [System.Windows.Forms.DialogResult]::None
            $save.Add_Click({
                $entry = $box.Text.Trim()
                if ($entry.Length -lt 20 -or $entry.Length -gt 512 -or $entry -match '[\s\p{C}]') {
                    [void][System.Windows.Forms.MessageBox]::Show('Use the copy icon beside your key in Google AI Studio, then paste the FULL key here. Do not copy the masked text with dots.','Gemini key entry')
                    $entry = $null; return
                }
                $entry = $null; $dialog.DialogResult = [System.Windows.Forms.DialogResult]::OK; $dialog.Close()
            })
            $dialog.Controls.Add($save); $dialog.AcceptButton = $save
            $cancel = New-Object System.Windows.Forms.Button
            $cancel.Text = 'Cancel'; $cancel.SetBounds(400,158,88,32); $cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
            $dialog.Controls.Add($cancel); $dialog.CancelButton = $cancel
            Write-Host 'Secure key form is ready.'
            try {
                if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { Write-Host 'Cancelled; settings unchanged.'; return }
                $secret = New-Object Security.SecureString
                foreach ($character in $box.Text.Trim().ToCharArray()) { $secret.AppendChar($character) }
                $secret.MakeReadOnly()
            } finally { $box.Clear(); $dialog.Dispose() }
        } else { $secret = Read-Host 'Gemini API key (hidden input; do not send it in chat)' -AsSecureString }
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
        $key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        if ($key.Length -lt 20 -or $key.Length -gt 512 -or $key -match '[\s\p{C}]') {
            throw 'Invalid API key format. Configuration was not saved.'
        }
        $settings['NEXUS_GEMINI_API_KEY'] = $key
        $settings['NEXUS_GEMINI_MODEL'] = $Model
        $settings['NEXUS_AI_ENABLED'] = 'true'
        $settings['NEXUS_AI_PROVIDER'] = 'Gemini'
    }
    [void](New-Item -ItemType Directory -Path $private -Force)
    $bytes = [Text.Encoding]::UTF8.GetBytes(($settings | ConvertTo-Json -Compress))
    try {
        $encrypted = [Security.Cryptography.ProtectedData]::Protect($bytes, $entropy,
            [Security.Cryptography.DataProtectionScope]::CurrentUser)
    } finally { [Array]::Clear($bytes, 0, $bytes.Length) }
    if ($encrypted.Length -gt 65536) { throw 'Encrypted settings exceed the supported size. Configuration was not saved.' }
    $temporary = Join-Path $private ('gemini-settings-' + [guid]::NewGuid().ToString('N') + '.tmp')
    [IO.File]::WriteAllBytes($temporary, $encrypted)
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = New-Object Security.AccessControl.FileSecurity
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($identity in @($sid, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'),
        [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity, 'FullControl', 'Allow'))
    }
    Set-AiFileAccess $temporary $acl
    $now = Get-ProtectedStoreHash $path
    if ($before -ne $now) { throw 'Local settings changed during entry. Configuration was not saved; run the tool again.' }
    if (Test-Path -LiteralPath $path) {
        Set-AiFileAccess $path $acl
        [IO.File]::Replace($temporary, $path, [NullString]::Value)
    } else { [IO.File]::Move($temporary, $path) }
    $temporary = $null
    Write-Host $(if ($Disable) { 'Local AI setting disabled; other integration settings preserved.' }
        else { 'Encrypted Gemini settings saved. Provider access has not been tested. Open Job Studio and explicitly consent before using Gemini.' })
    if ($Gui -and !$Disable) { [void][System.Windows.Forms.MessageBox]::Show('Encrypted Gemini settings saved. You can return to the website. No API request has been made.','NexusFreelance') }
    if ($overrides.Count -gt 0) { Write-Warning ('Environment values take precedence over this file: ' + ($overrides -join ', ') + '. The web process may have different environment values.') }
} catch {
    Write-Host ('Setup failure type: ' + $_.Exception.GetType().Name + '; line: ' + $_.InvocationInfo.ScriptLineNumber)
    if ($Gui) { [void][System.Windows.Forms.MessageBox]::Show('Configuration was not saved. Existing integrations were preserved. Check the setup status or use the PowerShell hidden-input command.','NexusFreelance') }
    throw
} finally {
    if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    if ($null -ne $secret) { $secret.Dispose() }
    $key = $null
    $settings.Clear()
    if ($null -ne $temporary -and (Test-Path -LiteralPath $temporary)) { Remove-Item -LiteralPath $temporary -Force }
}
