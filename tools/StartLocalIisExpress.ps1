# Start only the local MVC5 test site. A private config is generated inside tools/_runtime.
# Requires an existing IIS Express development HTTPS reservation for localhost:44391.
$ErrorActionPreference = 'Stop'
$solution = Split-Path -Parent $PSScriptRoot
$webroot = Join-Path $solution 'NexusFreelance.Web'
& (Join-Path $PSScriptRoot 'ConfigureMachineKey.ps1') -WebRoot $webroot
$baseConfig = Join-Path $env:USERPROFILE 'Documents\IISExpress\config\applicationhost.config'
$runtime = Join-Path $PSScriptRoot '_runtime'
$configPath = Join-Path $runtime 'applicationhost.config'
$iis = 'C:\Program Files\IIS Express\iisexpress.exe'
if (!(Test-Path $baseConfig)) { throw "Missing IIS Express user config: $baseConfig" }
if (!(Test-Path $iis)) { throw "Missing IIS Express: $iis" }
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
$content = Get-Content -LiteralPath $baseConfig -Raw
$originalPath = 'physicalPath="%IIS_SITES_HOME%\WebSite1"'
$originalBinding = '<binding protocol="http" bindingInformation=":8080:localhost" />'
if (!$content.Contains($originalPath) -or !$content.Contains($originalBinding)) {
    throw 'Unexpected default IIS Express config; check original site and binding before changing local config.'
}
$content = $content.Replace('name="WebSite1" id="1"', 'name="NexusFreelance" id="1"')
$content = $content.Replace($originalPath, 'physicalPath="' + $webroot + '"')
$content = $content.Replace($originalBinding, '<binding protocol="https" bindingInformation="*:44391:localhost" />')
[xml]$null = $content
if (Test-Path -LiteralPath $configPath) {
    $existing = Get-Content -LiteralPath $configPath -Raw
    if ($existing -and $existing.Trim() -eq $content.Trim()) {
        Write-Host 'Existing IIS Express test configuration is current.'
    } else {
        # Never truncate the active applicationhost.config. Prepare a complete
        # replacement first and let the move fail safely if IIS has locked it.
        $temporary = Join-Path $runtime ('applicationhost.' + [guid]::NewGuid().ToString('N') + '.tmp')
        try {
            [System.IO.File]::WriteAllText($temporary, $content, [System.Text.UTF8Encoding]::new($false))
            Move-Item -LiteralPath $temporary -Destination $configPath -Force -ErrorAction Stop
        } finally {
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
        }
    }
} else {
    [System.IO.File]::WriteAllText($configPath, $content, [System.Text.UTF8Encoding]::new($false))
}
& $iis "/config:$configPath" '/site:NexusFreelance' '/systray:false'
