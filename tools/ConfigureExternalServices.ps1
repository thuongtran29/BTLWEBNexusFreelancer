# Run with Windows PowerShell -STA. Passwords/keys are never passed on the command line.
[CmdletBinding()]
param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ExternalServiceSetup.ps1')
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$root = Split-Path -Parent $PSScriptRoot
$readPath = Get-NexusIntegrationStorePath $root
$settings = Read-NexusIntegrationStore $readPath
$path = Get-NexusIntegrationStorePath $root -ForWrite
$before = Get-NexusStoreHash $path
$form = New-Object Windows.Forms.Form
$form.Text = 'NexusFreelance - External services secure setup'
$form.ClientSize = New-Object Drawing.Size(760,520)
$form.StartPosition = 'CenterScreen'; $form.TopMost = $true
$form.Font = New-Object Drawing.Font('Segoe UI',10)
$form.FormBorderStyle = 'FixedDialog'; $form.MaximizeBox = $false
$intro = New-Object Windows.Forms.Label
$intro.Text = 'Select services to configure. Blank unchecked sections preserve existing settings. No provider request is sent when saving.'
$intro.SetBounds(18,14,724,48); $form.Controls.Add($intro)
$tabs = New-Object Windows.Forms.TabControl
$tabs.SetBounds(18,70,724,355); $form.Controls.Add($tabs)
$boxes = @{}; $checks = @{}
function Add-Page([string]$Name, [string]$Title) {
    $page = New-Object Windows.Forms.TabPage; $page.Text = $Title
    $check = New-Object Windows.Forms.CheckBox; $check.Text = 'Update this service'; $check.SetBounds(18,18,640,27)
    $page.Controls.Add($check); $checks[$Name] = $check; $tabs.TabPages.Add($page)
    return $page
}
function Add-Field($Page, [string]$Name, [string]$Label, [int]$Y, [string]$Value = '', [bool]$Secret = $false) {
    $labelControl = New-Object Windows.Forms.Label; $labelControl.Text = $Label; $labelControl.SetBounds(18,$Y,220,26)
    $Page.Controls.Add($labelControl)
    $box = New-Object Windows.Forms.TextBox; $box.SetBounds(245,$Y,435,29)
    $box.Text = $Value; $box.UseSystemPasswordChar = $Secret; $box.MaxLength = 2048
    $Page.Controls.Add($box); $boxes[$Name] = $box
}
function Add-Note($Page, [string]$Text, [int]$Y) {
    $labelControl = New-Object Windows.Forms.Label; $labelControl.Text = $Text; $labelControl.SetBounds(18,$Y,662,65)
    $Page.Controls.Add($labelControl)
}
$mail = Add-Page 'Mail' '1. Gmail verification'
Add-Field $mail 'Email' 'Gmail sender address' 65 $settings['NEXUS_SMTP_FROM']
Add-Field $mail 'Password' 'Google app password' 110 '' $true
Add-Field $mail 'Origin' 'Website HTTPS origin' 155 $(if($settings['NEXUS_PUBLIC_BASE_URL']){$settings['NEXUS_PUBLIC_BASE_URL']}else{'https://localhost:44391'})
Add-Note $mail 'Use the 16-letter app password, not your Google password. Spaces between groups are removed. SMTP uses smtp.gmail.com:587 with STARTTLS.' 210
$google = Add-Page 'Google' '2. Google login'
Add-Field $google 'GoogleJson' 'Web OAuth client JSON file' 65
$browse = New-Object Windows.Forms.Button; $browse.Text = 'Choose JSON file'; $browse.SetBounds(245,110,200,34); $google.Controls.Add($browse)
$browse.Add_Click({
    $file = New-Object Windows.Forms.OpenFileDialog; $file.Filter = 'Google OAuth JSON (*.json)|*.json'
    try { if($file.ShowDialog() -eq 'OK'){$boxes['GoogleJson'].Text = $file.FileName} } finally {$file.Dispose()}
})
Add-Note $google 'Choose the downloaded Web application client JSON. The callback https://localhost:44391/Account/GoogleCallback must also be authorized in Google Cloud.' 165
$ai = Add-Page 'Gemini' '3. Gemini AI JD'
Add-Field $ai 'GeminiKey' 'Full Gemini API key' 65 '' $true
Add-Field $ai 'GeminiModel' 'Model identifier' 110 $(if($settings['NEXUS_GEMINI_MODEL']){$settings['NEXUS_GEMINI_MODEL']}else{'gemini-3.5-flash-lite'})
Add-Note $ai 'Keep your Google project on the intended quota/billing tier. Saving does not generate text. The website still requires consent before sending a project brief to AI.' 165
$pay = Add-Page 'VNPay' '4. VNPay sandbox'
Add-Field $pay 'TmnCode' 'Sandbox merchant code' 65 $settings['NEXUS_VNPAY_TMN_CODE']
Add-Field $pay 'HashSecret' 'Sandbox hash secret' 110 '' $true
Add-Note $pay 'Optional: requires your registered sandbox merchant. This tool never enables production. An externally reachable HTTPS IPN endpoint is needed for provider-to-server payment confirmation.' 165
$status = New-Object Windows.Forms.Label; $status.SetBounds(18,435,470,65); $form.Controls.Add($status)
$save = New-Object Windows.Forms.Button; $save.Text = 'Save encrypted'; $save.SetBounds(495,450,145,40); $form.Controls.Add($save)
$cancel = New-Object Windows.Forms.Button; $cancel.Text = 'Close'; $cancel.SetBounds(650,450,92,40); $cancel.DialogResult = 'Cancel'; $form.Controls.Add($cancel)
$form.CancelButton = $cancel
$script:NexusSetupState=@{Path=$path;Settings=$settings;Hash=$before;Boxes=$boxes;Checks=$checks;Status=$status;Root=$root}
function Write-SetupStatus([string]$Code) {
    $state=$script:NexusSetupState
    $receipt=Join-Path $state.Root '_local/external-setup-status.txt'
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $receipt))
    [IO.File]::WriteAllText($receipt,($Code+' '+[DateTime]::UtcNow.ToString('o')))
}
$save.Add_Click({
    $state=$script:NexusSetupState;$entries=@{}
    try {
        Write-SetupStatus 'SAVE_STARTED'
        foreach($name in $state.Boxes.Keys){$entries[$name]=[string]$state.Boxes[$name].Text}
        $selected=@($state.Checks.Keys | Where-Object {$state.Checks[$_].Checked})
        $saved=Save-NexusExternalServices -Path $state.Path -ExpectedHash $state.Hash -Current $state.Settings -InputValues $entries -Selected $selected
        $state.Settings=$saved.Settings;$state.Hash=$saved.Hash
        Write-SetupStatus 'SAVED_AND_READBACK_VERIFIED'
        foreach($name in @('Password','GeminiKey','HashSecret')){$state.Boxes[$name].Clear()}
        foreach($check in $state.Checks.Values){$check.Checked=$false}
        $state.Status.Text='Saved AND verified on disk. No provider request made. You can close this form.'
        [void][Windows.Forms.MessageBox]::Show('Saved AND verified on disk. The website can now read this encrypted file. No connection has been tested yet.','NexusFreelance - Saved')
    } catch {
        $line=$_.InvocationInfo.ScriptLineNumber
        Write-SetupStatus ('SAVE_FAILED type='+$_.Exception.GetType().Name+' line='+$line)
        $state.Status.Text='Save failed. Settings were not verified. Check that Update this service is selected and all required fields are complete.'
        [void][Windows.Forms.MessageBox]::Show($state.Status.Text,'NexusFreelance - Not saved')
    } finally {$entries.Clear()}
})
if ($ValidateOnly) { 'SETUP_FORM_READY: controls='+$boxes.Count+'; service selectors='+$checks.Count; $form.Dispose(); $settings.Clear(); return }
Write-SetupStatus 'FORM_READY'
try { [void]$form.ShowDialog() } finally { foreach($box in $boxes.Values){$box.Clear()}; $settings.Clear(); $form.Dispose() }
