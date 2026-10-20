# Simulates a timed-out sitting for HTTP E2E tests. Development database only.
param(
    [Parameter(Mandatory = $true)][string]$Email,
    [Parameter(Mandatory = $true)][int]$TrackId,
    [string]$Server = '.\SQLEXPRESS',
    [string]$Database = 'NexusFreelanceMvp'
)
$ErrorActionPreference = 'Stop'
if ($Database -ne 'NexusFreelanceMvp' -or
    $Email -notmatch '^skills-freelancer-timer-other-[0-9a-f]{10}@example\.invalid$' -or $TrackId -le 0) {
    throw 'This helper only accepts isolated timer E2E accounts in NexusFreelanceMvp.'
}
$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = "Server=$Server;Database=$Database;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
try {
    $connection.Open()
    $cmd = $connection.CreateCommand()
    $cmd.CommandText = @'
IF DB_NAME() <> 'NexusFreelanceMvp' THROW 51093, 'Development DB only', 1;
UPDATE s SET ExpiresAtUtc=DATEADD(second, 1, s.StartedAtUtc)
FROM dbo.SkillExamSessions s
JOIN dbo.Users u ON u.Id=s.UserAccountId
WHERE u.Email=@Email AND s.SkillTrackId=@TrackId
  AND s.CompletedAtUtc IS NULL;
'@
    [void]$cmd.Parameters.Add('@Email',[System.Data.SqlDbType]::NVarChar,200)
    $cmd.Parameters['@Email'].Value = $Email
    [void]$cmd.Parameters.Add('@TrackId',[System.Data.SqlDbType]::Int)
    $cmd.Parameters['@TrackId'].Value = $TrackId
    if ($cmd.ExecuteNonQuery() -ne 1) { throw 'Expected exactly one already-started timer E2E session.' }
    Write-Host 'Expired one isolated E2E exam session.'
} finally {
    $connection.Dispose()
}
