# Read-only key/model probe. Does not generate text or change provider/billing settings.
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Security
Add-Type -AssemblyName System.Net.Http
$solution=Split-Path -Parent $PSScriptRoot
$path=Join-Path $solution '_local/integrations.dat'
$storeHelper = Join-Path $PSScriptRoot 'IntegrationStore.ps1'
if (Test-Path -LiteralPath $storeHelper) {
    . $storeHelper
    $path = Get-NexusIntegrationStorePath -SolutionRoot (Split-Path -Parent $PSScriptRoot)
}
$clear=$null;$settings=$null;$key=$null;$client=$null;$request=$null;$response=$null
try {
 $clear=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($path),[Text.Encoding]::UTF8.GetBytes('NexusFreelance.integrations.v1'),[Security.Cryptography.DataProtectionScope]::CurrentUser)
 $settings=[Text.Encoding]::UTF8.GetString($clear)|ConvertFrom-Json
 $key=$settings.NEXUS_GEMINI_API_KEY;$model=$settings.NEXUS_GEMINI_MODEL
 if([string]::IsNullOrWhiteSpace($key) -or $model -notmatch '\Agemini-[A-Za-z0-9._-]{1,26}\z'){throw 'Gemini configuration is incomplete.'}
 $handler=New-Object Net.Http.HttpClientHandler;$handler.AllowAutoRedirect=$false
 $client=New-Object Net.Http.HttpClient($handler);$client.Timeout=[TimeSpan]::FromSeconds(30)
 $request=New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::Get,'https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000')
 [void]$request.Headers.TryAddWithoutValidation('x-goog-api-key',$key)
 $response=$client.SendAsync($request).GetAwaiter().GetResult()
 if(!$response.IsSuccessStatusCode){Write-Output ('Gemini model-list HTTP status: '+[int]$response.StatusCode+'. Check key, project permissions or quota. Provider body omitted.');return}
 $payload=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json
 [pscustomobject]@{ConnectionSucceeded=$true;Model=$model;ConfiguredModelAvailable=(@($payload.models|Where-Object {$_.name -ceq ('models/'+$model)}).Count -gt 0);TextGenerationTested=$false;BillingOrFreeQuotaVerified=$false}
} catch {throw ('Gemini connection check failed ('+$_.Exception.GetType().Name+'). Credentials and provider response were omitted.')} finally {
 if($response){$response.Dispose()};if($request){$request.Dispose()};if($client){$client.Dispose()}
 if($clear){[Array]::Clear($clear,0,$clear.Length)};$settings=$null;$key=$null
}
