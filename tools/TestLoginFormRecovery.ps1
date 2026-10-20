#requires -Version 7.0
# Localhost-only HTTP regression. Uses the private, DPAPI-protected demo catalog.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Security
$base='https://localhost:44391'
$session=New-Object Microsoft.PowerShell.Commands.WebRequestSession
$plain=$null
function FetchToken($Page) { $tag=[regex]::Match([string]$Page.Content,'<input[^>]*name="__RequestVerificationToken"[^>]*>').Value; return [Net.WebUtility]::HtmlDecode([regex]::Match($tag,'value="([^"]+)"').Groups[1].Value) }
function SendForm($Body) { try { return Invoke-WebRequest -Uri ($base+'/Account/Login') -WebSession $session -Method Post -Body $Body -UseBasicParsing } catch { if($_.Exception.Response) { return @{StatusCode=[int]$_.Exception.Response.StatusCode;Content=''} }; throw } }
try {
    $plain=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path (Split-Path -Parent $PSScriptRoot) '_local/demo-accounts.dat')),[Text.Encoding]::UTF8.GetBytes('NexusFreelance.demo-accounts.v1'),[Security.Cryptography.DataProtectionScope]::CurrentUser)
    $settings=[Text.Encoding]::UTF8.GetString($plain)|ConvertFrom-Json
    $first=Invoke-WebRequest -Uri ($base+'/Account/Login') -WebSession $session -UseBasicParsing
    $oldToken=FetchToken $first
    $body=@{__RequestVerificationToken=$oldToken;Email='admin@nexus.example.test';Password=$settings.Password;Role='Admin'}
    $signed=SendForm $body
    if($signed.StatusCode -ne 200 -or $signed.Content -notmatch 'Việc quản trị cần làm') { throw 'Fresh admin login failed.' }
    Write-Host 'PASS fresh Admin sign-in'
    $stale=SendForm $body
    if($stale.StatusCode -ne 200 -or $stale.Content -notmatch 'biểu mẫu đăng nhập mới') { throw ('Stale login should recover to fresh form with explanation; actual HTTP '+$stale.StatusCode) }
    Write-Host 'PASS stale form after identity change recovers without resubmitting credentials'
    $noToken=SendForm @{Email='admin@nexus.example.test';Password=$settings.Password;Role='Admin'}
    if($noToken.StatusCode -ne 200 -or $noToken.Content -notmatch 'id="GoogleLoginButton"') { throw 'Missing token must be rejected and show fresh login, never execute login.' }
    Write-Host 'PASS missing token rejected before credential processing'
    $session=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $anonymous=SendForm @{Email='admin@nexus.example.test';Password=$settings.Password;Role='Admin'}
    if($anonymous.StatusCode -ne 200 -or $anonymous.Content -notmatch 'id="GoogleLoginButton"') { throw 'Anonymous request without token did not recover safely.' }
    Write-Host 'PASS anonymous login without token shows fresh form'
    $adminPage=Invoke-WebRequest -Uri ($base+'/Admin') -WebSession $session -UseBasicParsing
    if($adminPage.Content -notmatch 'id="GoogleLoginButton"') { throw 'Rejected login must not authenticate guest.' }
    Write-Host 'PASS rejected form did not create authenticated session'
    $registration=Invoke-WebRequest -Uri ($base+'/Account/Register') -WebSession $session -Method Post -Body @{Name='Invalid token probe'} -UseBasicParsing -SkipHttpErrorCheck
    if($registration.StatusCode -ne 400 -or $registration.Content -notmatch 'Yêu cầu vừa gửi chưa được thực hiện') { throw 'Other invalid form tokens must remain HTTP 400 with clear recovery view.' }
    Write-Host 'PASS non-login invalid token remains HTTP 400 with usable recovery page'
    Write-Output 'LOGIN_RECOVERY_CHECKS=6 PASS'
} finally { if($plain){[Array]::Clear($plain,0,$plain.Length)}; $settings=$null; $body=$null }



exit 0
