# Removes only an E2E-created administrator privilege after the test run.
param(
    [Parameter(Mandatory = $true)][string]$Email,
    [string]$Server = '.\SQLEXPRESS',
    [string]$Database = 'NexusFreelanceMvp'
)
$ErrorActionPreference = 'Stop'
if ($Database -ne 'NexusFreelanceMvp') { throw 'ResetTestAdmin is restricted to the E2E database.' }
$isLegacyReportAdmin = $Email.Trim().ToLowerInvariant() -match '^report-admin-[0-9a-f]{10}@example\.invalid$'
$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = "Server=$Server;Database=$Database;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
try {
    $connection.Open()
    $cmd = $connection.CreateCommand()
    $cmd.CommandText = @'
UPDATE dbo.Users SET Role = 'Client', IsSuspended = 1
WHERE Email = @Email AND Role = 'Admin'
AND (Name = 'Admin E2E' OR (Name = 'Report Test Client' AND @IsLegacyReportAdmin = 1));
'@
    [void]$cmd.Parameters.Add('@Email', [System.Data.SqlDbType]::NVarChar, 200)
    $cmd.Parameters['@Email'].Value = $Email.Trim().ToLowerInvariant()
    [void]$cmd.Parameters.Add('@IsLegacyReportAdmin', [System.Data.SqlDbType]::Bit)
    $cmd.Parameters['@IsLegacyReportAdmin'].Value = [bool]$isLegacyReportAdmin
    if ($cmd.ExecuteNonQuery() -ne 1) { throw 'Did not locate exactly one temporary E2E Admin account.' }
    Write-Host 'Temporary Admin privilege removed.'
} finally {
    $connection.Dispose()
}
