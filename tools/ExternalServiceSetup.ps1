. (Join-Path $PSScriptRoot 'IntegrationStore.ps1')
function Save-NexusExternalServices([string]$Path,[string]$ExpectedHash,[hashtable]$Current,[hashtable]$InputValues,[string[]]$Selected) {
    if (!$Selected -or $Selected.Count -eq 0) { throw 'Select at least one service to update.' }
    if (@($Selected | Where-Object {$_ -notin @('Mail','Google','Gemini','VNPay')}).Count) { throw 'Unknown service selection.' }
    $next=@{};foreach($key in $Current.Keys){$next[$key]=$Current[$key]}
    if (@($Selected | Where-Object {$_ -in @('Mail','Google','VNPay')}).Count) {
        $origin=([string]$InputValues['Origin']).Trim().TrimEnd('/');$uri=$null
        if (![Uri]::TryCreate($origin,[UriKind]::Absolute,[ref]$uri) -or $uri.Scheme -ne 'https' -or $uri.AbsolutePath -ne '/' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) { throw 'Use an HTTPS website origin without path, query or credentials.' }
        $next['NEXUS_PUBLIC_BASE_URL']=$origin
    }
    if ('Mail' -in $Selected) {
        $email=([string]$InputValues['Email']).Trim();$address=[Net.Mail.MailAddress]::new($email)
        if ($address.Address -cne $email -or $address.Host -notin @('gmail.com','googlemail.com')) { throw 'Enter the Gmail address that owns the app password.' }
        $password=([string]$InputValues['Password']) -replace '\s',''
        if ($password -cnotmatch '^[a-zA-Z]{16}$') { throw 'Enter the complete 16-letter Google app password.' }
        $next['NEXUS_MAIL_MODE']='smtp';$next['NEXUS_SMTP_HOST']='smtp.gmail.com';$next['NEXUS_SMTP_PORT']='587'
        $next['NEXUS_SMTP_FROM']=$email;$next['NEXUS_SMTP_USERNAME']=$email;$next['NEXUS_SMTP_PASSWORD']=$password
    }
    if ('Google' -in $Selected) {
        $jsonPath=([string]$InputValues['GoogleJson']).Trim()
        if (!(Test-Path -LiteralPath $jsonPath -PathType Leaf) -or (Get-Item -LiteralPath $jsonPath).Length -gt 65536) { throw 'Choose the OAuth Web client JSON file.' }
        $json=[IO.File]::ReadAllText($jsonPath)|ConvertFrom-Json;$web=$json.web
        if (!$web -or $web.client_id -notmatch '\.apps\.googleusercontent\.com$' -or !$web.client_secret) { throw 'The JSON must contain a Google Web application OAuth client.' }
        $callback=$origin+'/Account/GoogleCallback'
        if ($callback -cnotin @($web.redirect_uris)) { throw 'The JSON does not authorize this callback. Add it in Google Cloud and download the JSON again.' }
        $next['NEXUS_GOOGLE_CLIENT_ID']=[string]$web.client_id;$next['NEXUS_GOOGLE_CLIENT_SECRET']=[string]$web.client_secret
        $next['NEXUS_GOOGLE_REDIRECT_URI']=$callback
    }
    if ('Gemini' -in $Selected) {
        $key=([string]$InputValues['GeminiKey']).Trim();$model=([string]$InputValues['GeminiModel']).Trim()
        if ($key.Length -lt 20 -or $key.Length -gt 512 -or $key -match '[\s\p{C}]') { throw 'Paste the complete Gemini key using the copy button in AI Studio.' }
        if ($model -cnotmatch '^gemini-[A-Za-z0-9._-]{1,26}$') { throw 'Enter a Gemini model identifier.' }
        $next['NEXUS_GEMINI_API_KEY']=$key;$next['NEXUS_GEMINI_MODEL']=$model;$next['NEXUS_AI_ENABLED']='true';$next['NEXUS_AI_PROVIDER']='Gemini'
    }
    if ('VNPay' -in $Selected) {
        $merchant=([string]$InputValues['TmnCode']).Trim();$secret=[string]$InputValues['HashSecret']
        if ($merchant -cnotmatch '^[A-Za-z0-9]{8}$' -or $secret.Length -lt 32 -or $secret.Length -gt 256 -or $secret -match '\s') { throw 'Enter the registered sandbox merchant code and hash secret.' }
        $next['NEXUS_VNPAY_ENABLED']='true';$next['NEXUS_VNPAY_MODE']='sandbox';$next['NEXUS_VNPAY_TMN_CODE']=$merchant;$next['NEXUS_VNPAY_HASH_SECRET']=$secret
    }
    Write-NexusIntegrationStore -Path $Path -Settings $next -ExpectedHash $ExpectedHash
    # A successful dialog requires actual disk read/decryption, not only a click or write attempt.
    $readback=Read-NexusIntegrationStore $Path
    if ($readback.Count -ne $next.Count) { throw 'Saved settings could not be verified. No success was recorded.' }
    foreach($key in $next.Keys) {
        if (!$readback.ContainsKey($key) -or $readback[$key] -cne $next[$key]) { throw 'Saved settings could not be verified. No success was recorded.' }
    }
    $hash=Get-NexusStoreHash $Path
    if (!$hash) { throw 'Saved settings file is absent. No success was recorded.' }
    return @{Settings=$readback;Hash=$hash}
}
