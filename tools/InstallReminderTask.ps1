param([switch]$Remove)
$ErrorActionPreference = 'Stop'
$script = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'RunReminderWorker.ps1')).Path
$root = Split-Path -Parent $PSScriptRoot
# Each checkout has its own task. No credentials are stored by this installer.
$name = 'NexusFreelance-Reminders-' + ([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($root)).Replace('/','_').Replace('\','_').TrimEnd('='))
if ($Remove) { Unregister-ScheduledTask -TaskName $name -Confirm:$false; return }
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ('-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $script + '"') -WorkingDirectory $root
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 5)
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit (New-TimeSpan -Minutes 4)
$principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Select-Object TaskName, State
Write-Output 'Registered for the current signed-in Windows user. For unattended production, use an approved service account and SQL permissions. Set NEXUS_REMINDERS_EXTERNAL=true in the web host environment to disable the IIS timer.'
