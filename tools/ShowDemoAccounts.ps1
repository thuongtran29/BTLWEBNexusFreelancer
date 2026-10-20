#requires -Version 5.1
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Security
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$path=Join-Path (Split-Path -Parent $PSScriptRoot) '_local\demo-accounts.dat'
if(!(Test-Path -LiteralPath $path)) { throw 'Protected demo credentials are available only on the machine where this dataset was prepared.' }
if((Get-Item -LiteralPath $path).Length -gt 65536) { throw 'Invalid protected credential file.' }
$plain=$null
$form=$null
try {
    $plain=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($path),[Text.Encoding]::UTF8.GetBytes('NexusFreelance.demo-accounts.v1'),[Security.Cryptography.DataProtectionScope]::CurrentUser)
    $settings=[Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json
    $form=New-Object Windows.Forms.Form
    $form.Text='NexusFreelance - Local demo accounts'
    $form.ClientSize=New-Object Drawing.Size(660,500)
    $form.StartPosition='CenterScreen'
    $intro=New-Object Windows.Forms.Label
    $intro.Text='LOCALHOST DEMO ONLY. Personal profiles keep their original password.'
    $intro.SetBounds(20,18,620,28)
    $list=New-Object Windows.Forms.ListBox
    $list.SetBounds(20,55,620,310)
    foreach($account in $settings.Accounts) { $list.Items.Add(($account.Role+' | '+$account.Email)) | Out-Null }
    $label=New-Object Windows.Forms.Label
    $label.Text='Password for these demo accounts:'
    $label.SetBounds(20,385,620,24)
    $box=New-Object Windows.Forms.TextBox
    $box.SetBounds(20,414,620,28)
    $box.ReadOnly=$true
    $box.UseSystemPasswordChar=$true
    $box.Text=$settings.Password
    $show=New-Object Windows.Forms.CheckBox
    $show.Text='Show password'
    $show.SetBounds(20,457,180,25)
    $show.Add_CheckedChanged({$box.UseSystemPasswordChar=!$show.Checked})
    $form.Controls.AddRange(@($intro,$list,$label,$box,$show))
    $form.ShowDialog() | Out-Null
} finally {
    if($null -ne $plain) { [Array]::Clear($plain,0,$plain.Length) }
    $settings=$null
    if($null -ne $form) {$form.Dispose()}
}
