param([Parameter(Mandatory=$true)][string]$Email, [string]$Name = 'Local administrator')
# Trusted local operator bootstrap; never exposes an HTTP role-grant endpoint.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'NexusFreelance.Web\bin\NexusFreelance.Web.dll'))
if ($Email.Length -gt 200 -or $Name.Length -gt 120 -or [string]::IsNullOrWhiteSpace($Name)) { throw 'Invalid email or display name.' }
$parsedEmail = [Net.Mail.MailAddress]::new($Email)
if ($parsedEmail.Address -ne $Email) { throw 'Use a plain email address.' }
$securePassword = Read-Host 'New administrator password (12-128 characters, mixed case, digits and symbol)' -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    $policy = [NexusFreelance.Web.ViewModels.StrongPasswordAttribute]::new()
    if (!$policy.IsValid($plainPassword)) { throw $policy.ErrorMessage }
    $passwordHash = [NexusFreelance.Web.Services.PasswordService]::Hash($plainPassword)
} finally { $plainPassword = $null; [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer) }
[xml]$config = Get-Content -LiteralPath (Join-Path $root 'NexusFreelance.Web\Web.config') -Raw
$connection = [Data.SqlClient.SqlConnection]::new(($config.configuration.connectionStrings.add | Where-Object name -eq 'NexusDb').connectionString)
try {
    $connection.Open()
    if ($connection.Database -ne 'NexusFreelanceMvp') { throw 'Bootstrap is restricted to the local development database.' }
    $command = $connection.CreateCommand()
    $command.CommandText = "SET QUOTED_IDENTIFIER ON; IF EXISTS(SELECT 1 FROM dbo.Users WHERE Email=@Email AND Role='Admin') THROW 51300,'Administrator already exists; use password recovery instead',1; INSERT dbo.Users(Name,Email,PasswordHash,Role,JoinedAtUtc,IsSuspended,EmailVerifiedAtUtc) VALUES(@Name,@Email,@Hash,'Admin',GETUTCDATE(),0,GETUTCDATE());"
    [void]$command.Parameters.Add('@Email',[Data.SqlDbType]::NVarChar,200); $command.Parameters['@Email'].Value=$Email.Trim().ToLowerInvariant()
    [void]$command.Parameters.Add('@Name',[Data.SqlDbType]::NVarChar,120); $command.Parameters['@Name'].Value=$Name.Trim()
    [void]$command.Parameters.Add('@Hash',[Data.SqlDbType]::NVarChar,400); $command.Parameters['@Hash'].Value=$passwordHash
    [void]$command.ExecuteNonQuery()
    Write-Host 'Local administrator created. Select Admin on the login page.'
} finally { $connection.Dispose(); $passwordHash=$null }
