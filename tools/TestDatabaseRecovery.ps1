param(
    [string]$Server = '.\SQLEXPRESS',
    [ValidatePattern('^NexusFreelance(Mvp|Audit[0-9]{8})$')][string]$Database = 'NexusFreelanceMvp',
    [string]$ResultPath = (Join-Path (Split-Path -Parent $PSScriptRoot) '_local/database-recovery.json')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data
$target = 'NexusAuditRecovery' + [Guid]::NewGuid().ToString('N')
if ($target -notmatch '^NexusAuditRecovery[0-9a-f]{32}$') { throw 'Invalid isolated restore target.' }
$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$builder['Data Source']=$Server; $builder['Initial Catalog']='master'; $builder['Integrated Security']=$true; $builder['Encrypt']=$true; $builder['TrustServerCertificate']=$true
$connection=New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
$connection.Open()
function Query([string]$text) {
    $command=$connection.CreateCommand();$command.CommandText=$text;$command.CommandTimeout=300
    $table=New-Object System.Data.DataTable
    try { $reader=$command.ExecuteReader(); try {$table.Load($reader)} finally {$reader.Dispose()} } finally {$command.Dispose()}
    return ,$table
}
function Quote([string]$value) { return "N'" + $value.Replace("'","''") + "'" }
$created=$false
try {
    $source=Query "SELECT COUNT(*) AS N FROM sys.databases WHERE name=$(Quote $Database)"
    if($source.Rows[0].N -ne 1){throw 'Source database missing.'}
    $exists=Query "SELECT COUNT(*) AS N FROM sys.databases WHERE name=$(Quote $target)"
    if($exists.Rows[0].N -ne 0){throw 'Restore target already exists. Never overwrite.'}
    $settings=Query "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000)) AS BackupPath"
    $directory=[string]$settings.Rows[0].BackupPath
    if(!$directory){throw 'SQL default backup directory is unavailable.'}
    $backup=[IO.Path]::Combine($directory, $target+'.bak')
    $counts=Query "SELECT s.name+'.'+t.name AS Name, SUM(p.rows) AS N FROM [$Database].sys.tables t JOIN [$Database].sys.schemas s ON s.schema_id=t.schema_id JOIN [$Database].sys.partitions p ON p.object_id=t.object_id AND p.index_id IN(0,1) GROUP BY s.name,t.name ORDER BY Name"
    # COPY_ONLY preserves the existing backup chain. No WITH REPLACE is ever used.
    $null=Query "BACKUP DATABASE [$Database] TO DISK=$(Quote $backup) WITH COPY_ONLY,CHECKSUM"
    $null=Query "RESTORE VERIFYONLY FROM DISK=$(Quote $backup) WITH CHECKSUM"
    $files=Query "RESTORE FILELISTONLY FROM DISK=$(Quote $backup)"
    $paths=Query "SELECT TOP 1 physical_name FROM sys.master_files WHERE database_id=DB_ID($(Quote $Database)) AND type=0"
    $dataDir=[IO.Path]::GetDirectoryName([string]$paths.Rows[0].physical_name)
    $moves=@();$index=0
    foreach($file in $files.Rows) {
        if($file.Type -notin @('D','L')){throw 'Only ordinary data/log files are supported.'}
        $index++;$suffix=if($file.Type -eq 'L'){'.ldf'}else{'.mdf'}
        $physical=[IO.Path]::Combine($dataDir,$target+'_'+$index+$suffix)
        $moves += 'MOVE '+(Quote ([string]$file.LogicalName))+' TO '+(Quote $physical)
    }
    $created=$true
    $null=Query ("RESTORE DATABASE [$target] FROM DISK=$(Quote $backup) WITH CHECKSUM, "+($moves -join ', '))
    $integrity=Query "DBCC CHECKDB ([$target]) WITH TABLERESULTS, NO_INFOMSGS, ALL_ERRORMSGS"
    if($integrity.Rows.Count -ne 0){throw 'Restored database failed integrity checks.'}
    $violations=Query "USE [$target]; DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS; USE master;"
    if($violations.Rows.Count -ne 0){throw 'Restored data violates constraints.'}
    $restored=Query "SELECT s.name+'.'+t.name AS Name, SUM(p.rows) AS N FROM [$target].sys.tables t JOIN [$target].sys.schemas s ON s.schema_id=t.schema_id JOIN [$target].sys.partitions p ON p.object_id=t.object_id AND p.index_id IN(0,1) GROUP BY s.name,t.name ORDER BY Name"
    $before=@($counts.Rows | ForEach-Object { $_.Name+'|'+$_.N });$after=@($restored.Rows | ForEach-Object {$_.Name+'|'+$_.N})
    if(Compare-Object $before $after){throw 'Table counts differ. Stop concurrent writes and rerun the drill.'}
    $result=[ordered]@{atUtc=[DateTime]::UtcNow.ToString('o');source=$Database;isolatedTarget=$target;backup=$backup;tables=$restored.Rows.Count;restore=$true;checksum=$true;checkDb=$true;checkConstraints=$true;rowCountsMatch=$true;sourceOverwritten=$false}
    $destination=[IO.Path]::GetFullPath($ResultPath);New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    $result | ConvertTo-Json | Set-Content -LiteralPath $destination -Encoding UTF8
    Write-Output ('RECOVERY_PASSED tables='+$restored.Rows.Count+'; source preserved; disposable restore only.')
} finally {
    if($created -and $target -match '^NexusAuditRecovery[0-9a-f]{32}$') { $null=Query "USE master; IF DB_ID($(Quote $target)) IS NOT NULL BEGIN ALTER DATABASE [$target] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$target]; END" }
    $connection.Dispose()
}
