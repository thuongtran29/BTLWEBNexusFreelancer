[CmdletBinding()]
param([switch]$Probe, [switch]$SendTestMail)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'IntegrationStore.ps1')
Add-Type -AssemblyName System.Net.Http
$root = Split-Path -Parent $PSScriptRoot
$path = Get-NexusIntegrationStorePath $root
$values = Read-NexusIntegrationStore $path
function Setting([string]$Key) {
    $value = [Environment]::GetEnvironmentVariable($Key)
    if (![string]::IsNullOrWhiteSpace($value)) { return $value }
    return [string]$values[$Key]
}
function Present([string]$Key) { return ![string]::IsNullOrWhiteSpace((Setting $Key)) }
$results = New-Object 'Collections.Generic.List[object]'
function Result([string]$Service,[string]$Check,[string]$Status,[string]$Detail) {
    $results.Add([pscustomobject]@{Service=$Service;Check=$Check;Status=$Status;Detail=$Detail})
}
function Web-Probe([string]$Url, [string]$Header='', [string]$Secret='') {
    $handler = New-Object Net.Http.HttpClientHandler; $handler.AllowAutoRedirect = $false
    $client = New-Object Net.Http.HttpClient($handler); $client.Timeout = [TimeSpan]::FromSeconds(20)
    $request = New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::Get,$Url)
    $response = $null
    try {
        if($Header){[void]$request.Headers.TryAddWithoutValidation($Header,$Secret)}
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $body = if($response.IsSuccessStatusCode){$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}else{''}
        return @{Code=[int]$response.StatusCode;Body=$body}
    } finally {if($response){$response.Dispose()};$request.Dispose();$client.Dispose()}
}
try {
    Result 'Configuration' 'Protected store' $(if(Test-Path -LiteralPath $path){'PRESENT'}else{'MISSING'}) $path
    $origin = Setting 'NEXUS_PUBLIC_BASE_URL'; $uri=$null
    $originOk = [Uri]::TryCreate($origin,[UriKind]::Absolute,[ref]$uri) -and $uri.Scheme -eq 'https' -and $uri.AbsolutePath -eq '/' -and !$uri.UserInfo -and !$uri.Query -and !$uri.Fragment
    $mailMode = Setting 'NEXUS_MAIL_MODE'
    $port=0
    $portOk=[int]::TryParse((Setting 'NEXUS_SMTP_PORT'),[ref]$port) -and $port -gt 0 -and $port -le 65535
    $senderOk=$false;try {$sender=New-Object Net.Mail.MailAddress((Setting 'NEXUS_SMTP_FROM'));$senderOk=$true}catch{}
    $smtpReady = $originOk -and (Present 'NEXUS_SMTP_HOST') -and $portOk -and $senderOk
    if ((Setting 'NEXUS_SMTP_HOST') -ieq 'smtp.gmail.com') { $smtpReady = $smtpReady -and (Present 'NEXUS_SMTP_USERNAME') -and (Present 'NEXUS_SMTP_PASSWORD') }
    if($mailMode -eq 'pickup') {Result 'Email' 'Mode' 'TEST_ONLY' 'Pickup files are local; no email is delivered to Gmail.'}
    elseif($smtpReady -and (!$mailMode -or $mailMode -eq 'smtp')) {Result 'Email' 'Configuration' 'CONFIGURED' 'SMTP values present; provider authentication requires a probe.'}
    else {Result 'Email' 'Configuration' 'MISSING' 'Configure Gmail sender, app password, SMTP host/port and public HTTPS origin.'}
    if(($Probe -or $SendTestMail) -and $smtpReady -and $mailMode -ne 'pickup') {
        if($SendTestMail) {
            $message=$null;$smtp=$null
            try {
                $message=New-Object Net.Mail.MailMessage
                $message.From=New-Object Net.Mail.MailAddress((Setting 'NEXUS_SMTP_FROM'))
                [void]$message.To.Add((Setting 'NEXUS_SMTP_FROM'))
                $message.Subject='NexusFreelance - Kiem tra dich vu email'
                $message.Body='This is a service connection test. No account, verification code, password or financial transaction was created.'
                $smtp=New-Object Net.Mail.SmtpClient((Setting 'NEXUS_SMTP_HOST'),[int](Setting 'NEXUS_SMTP_PORT'))
                $smtp.EnableSsl=$true;$smtp.UseDefaultCredentials=$false;$smtp.Timeout=20000
                $smtp.Credentials=New-Object Net.NetworkCredential((Setting 'NEXUS_SMTP_USERNAME'),(Setting 'NEXUS_SMTP_PASSWORD'))
                $smtp.Send($message)
                Result 'Email' 'Authenticated test email' 'PASS' 'SMTP accepted one test email to the configured sender. Inbox receipt must be checked separately.'
            } catch {Result 'Email' 'Authenticated test email' 'FAIL' ('SMTP failed ('+$_.Exception.GetType().Name+'). Check app password/account or network; no secret/error body logged.')}
            finally {if($smtp){$smtp.Dispose()};if($message){$message.Dispose()}}
        } else {
            $tcp=New-Object Net.Sockets.TcpClient
            try {
                $task=$tcp.ConnectAsync((Setting 'NEXUS_SMTP_HOST'),[int](Setting 'NEXUS_SMTP_PORT'))
                if(!$task.Wait(15000)){throw 'SMTP network timeout'}
                Result 'Email' 'TCP connection' 'PASS' 'SMTP port reachable. Use -SendTestMail to verify TLS, credentials and sending.'
            } catch {Result 'Email' 'TCP connection' 'FAIL' 'SMTP port not reachable; check DNS, firewall and network.'} finally {$tcp.Dispose()}
        }
    }
    $callback=Setting 'NEXUS_GOOGLE_REDIRECT_URI'
    $callbackUri=$null
    $callbackOk=[Uri]::TryCreate($callback,[UriKind]::Absolute,[ref]$callbackUri) -and $callbackUri.Scheme -eq 'https' -and $callbackUri.AbsolutePath -ceq '/Account/GoogleCallback' -and !$callbackUri.UserInfo -and !$callbackUri.Query -and !$callbackUri.Fragment
    $googleReady = (Present 'NEXUS_GOOGLE_CLIENT_ID') -and (Present 'NEXUS_GOOGLE_CLIENT_SECRET') -and $callbackOk
    Result 'Google' 'Configuration' $(if($googleReady){'CONFIGURED'}else{'MISSING'}) 'OAuth client, secret and exact callback are required; successful login requires Google consent in a browser.'
    if($Probe){
        try {$p=Web-Probe 'https://accounts.google.com/.well-known/openid-configuration';Result 'Google' 'Discovery endpoint' $(if($p.Code -eq 200){'PASS'}else{'FAIL'}) ('HTTP '+$p.Code+'; not a completed OAuth login.')}
        catch {Result 'Google' 'Discovery endpoint' 'FAIL' 'Google endpoint unavailable from this machine.'}
    }
    $provider=Setting 'NEXUS_AI_PROVIDER';if(!$provider){$provider='OpenAI'}
    $aiReady=(Setting 'NEXUS_AI_ENABLED') -eq 'true' -and $provider -in @('Gemini','OpenAI')
    $keyName=if($provider -eq 'Gemini'){'NEXUS_GEMINI_API_KEY'}else{'NEXUS_OPENAI_API_KEY'}
    $model=Setting $(if($provider -eq 'Gemini'){'NEXUS_GEMINI_MODEL'}else{'NEXUS_OPENAI_MODEL'})
    $aiReady=$aiReady -and (Present $keyName) -and $model -cmatch '^[A-Za-z0-9._:-]{1,33}$'
    Result 'AI JD' 'Configuration' $(if($aiReady){'CONFIGURED'}else{'MISSING'}) ('Selected provider: '+$provider+'. No automatic switch to another provider.')
    if($Probe -and $aiReady){
        try {
            $p=if($provider -eq 'Gemini'){Web-Probe 'https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000' 'x-goog-api-key' (Setting $keyName)}else{Web-Probe 'https://api.openai.com/v1/models' 'Authorization' ('Bearer '+(Setting $keyName))}
            if($p.Code -eq 200){
                $payload=$p.Body|ConvertFrom-Json
                $available=if($provider -eq 'Gemini'){@($payload.models|Where-Object {$_.name -ceq ('models/'+$model)}).Count -gt 0}else{@($payload.data|Where-Object {$_.id -ceq $model}).Count -gt 0}
                Result 'AI JD' 'Key and configured model' $(if($available){'PASS'}else{'FAIL'}) ('HTTP 200; configured model '+$(if($available){'listed'}else{'not listed'})+'. Text generation/quota still requires Job Studio test.')
            } else {Result 'AI JD' 'Key and configured model' 'FAIL' ('HTTP '+$p.Code+'; check key, API restrictions and quota. Provider response omitted.')}
        } catch {Result 'AI JD' 'Key and configured model' 'FAIL' 'Could not reach or parse provider model list; no credential/provider body logged.'}
    }
    $payMode=Setting 'NEXUS_VNPAY_MODE';if(!$payMode){$payMode='sandbox'}
    $payReady=$payMode -in @('sandbox','production') -and (Setting 'NEXUS_VNPAY_ENABLED') -eq 'true' -and (Setting 'NEXUS_VNPAY_TMN_CODE') -match '^[A-Za-z0-9]{8}$' -and (Setting 'NEXUS_VNPAY_HASH_SECRET').Length -ge 32 -and (Setting 'NEXUS_VNPAY_HASH_SECRET').Length -le 256 -and $originOk
    if($payMode -eq 'production'){$payReady=$payReady -and [Environment]::GetEnvironmentVariable('NEXUS_VNPAY_LIVE_ENABLED') -ceq 'true' -and [Environment]::GetEnvironmentVariable('NEXUS_PSP_MERCHANT_APPROVED') -ceq 'true' -and !$uri.IsLoopback}
    Result 'VNPay' 'Configuration' $(if($payReady){'CONFIGURED'}else{'MISSING'}) 'Own merchant credentials and HTTPS IPN are required. No payment, refund or real money movement is performed by this checker.'
    if($Probe){
        try {$p=Web-Probe 'https://sandbox.vnpayment.vn/paymentv2/vpcpay.html';Result 'VNPay' 'Sandbox endpoint' $(if($p.Code -ge 200 -and $p.Code -lt 400){'REACHABLE'}else{'HTTP_REJECTED'}) ('HTTP '+$p.Code+'; endpoint response only, not a validated payment or merchant credential.')}
        catch {Result 'VNPay' 'Sandbox endpoint' 'FAIL' 'VNPay sandbox could not be reached.'}
    }
    Result 'MB QR' 'Provider dependency' 'LOCAL' 'QR generated locally; balance credited only after Admin reconciliation. No bank API confirms a transfer.'
    $output=Join-Path $root '_local/external-service-checks.json'
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $output))
    [IO.File]::WriteAllText($output,(@{CheckedAtUtc=[DateTime]::UtcNow.ToString('o');Checks=@($results.ToArray())}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
    $results.ToArray()|Format-Table -AutoSize -Wrap
    Write-Output ('Report: '+$output)
} finally {$values.Clear()}
