# First-admin bootstrap for local development. Register a normal account with
# the web application first so its password is hashed by the application's own
# PasswordService. This script never accepts or stores a password.
param(
    [Parameter(Mandatory = $true)][string]$Email,
    [string]$Server = '.\SQLEXPRESS',
    [string]$Database = 'NexusFreelanceMvp'
)
$ErrorActionPreference = 'Stop'
$emailNormalized = $Email.Trim().ToLowerInvariant()
if ($emailNormalized.Length -gt 200 -or $emailNormalized -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$') {
    throw 'Invalid email address.'
}
$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = "Server=$Server;Database=$Database;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = @'
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE Role = 'Admin')
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51100, 'An administrator already exists. Bootstrap is disabled.', 1;
END;
UPDATE dbo.Users SET Role = 'Admin',
    EmailVerifiedAtUtc = COALESCE(EmailVerifiedAtUtc, SYSUTCDATETIME())
WHERE Email = @Email AND Role = 'Client' AND IsSuspended = 0;
IF @@ROWCOUNT <> 1
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51101, 'Register an active Client account before bootstrap.', 1;
END;
COMMIT TRANSACTION;
'@
    [void]$command.Parameters.Add('@Email', [System.Data.SqlDbType]::NVarChar, 200)
    $command.Parameters['@Email'].Value = $emailNormalized
    [void]$command.ExecuteNonQuery()
    Write-Host "Created initial Admin from existing account: $emailNormalized"
} finally {
    if ($connection) { $connection.Dispose() }
}
