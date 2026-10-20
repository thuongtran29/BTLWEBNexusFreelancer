#requires -Version 5.1
param([Parameter(Mandatory=$true)][string]$OutputPath,
 [ValidatePattern('^NexusFreelance(Mvp|Submission[0-9]*)$')][string]$SourceDatabase='NexusFreelanceSubmission20261019',
 [ValidatePattern('^NexusFreelanceSubmission[0-9]*$')][string]$TargetDatabase='NexusFreelanceSubmission', [switch]$SchemaOnly)
$ErrorActionPreference='Stop'
if($SourceDatabase -eq 'NexusFreelanceMvp' -and !$SchemaOnly) { throw 'Live personal data must never be exported into the submission.' }
$absoluteOutput=[IO.Path]::GetFullPath($OutputPath)
$temporaryOutput=$absoluteOutput+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
$smoRoot='C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE'
Add-Type @'
using System;
using System.IO;
using System.Reflection;
public static class NexusSmoResolver {
    public static void Enable(string root) {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args) {
            var name = new AssemblyName(args.Name).Name + ".dll";
            foreach(var directory in new[]{root,Path.Combine(root,"PublicAssemblies")}) {
                var path=Path.Combine(directory,name);
                if(File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
    }
}
'@
[NexusSmoResolver]::Enable($smoRoot)
[Reflection.Assembly]::LoadFrom('C:\Program Files\Microsoft SQL Server Management Studio 22\Release\SharedAssemblies\System.Memory\4.0.5.0\4.6.3\netfx\System.Memory.dll') | Out-Null
[Reflection.Assembly]::LoadFrom('C:\Program Files\Microsoft SQL Server Management Studio 22\Release\SharedAssemblies\System.Runtime.CompilerServices.Unsafe\6.0.3.0\6.1.2\netfx\System.Runtime.CompilerServices.Unsafe.dll') | Out-Null
foreach($assembly in @('Microsoft.Data.SqlClient.dll','Microsoft.SqlServer.Management.Sdk.Sfc.dll','Microsoft.SqlServer.ConnectionInfo.dll','Microsoft.SqlServer.SqlEnum.dll','Microsoft.SqlServer.Smo.dll')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $smoRoot $assembly)) | Out-Null
}
$server=[Microsoft.SqlServer.Management.Smo.Server]::new('.\SQLEXPRESS')
$server.ConnectionContext.LoginSecure=$true
$server.ConnectionContext.EncryptConnection=$true
$server.ConnectionContext.TrustServerCertificate=$true
try {
    $database=$server.Databases[$SourceDatabase]
    if($null -eq $database) { throw 'Clean candidate database not found.' }
    $scripter=[Microsoft.SqlServer.Management.Smo.Scripter]::new($server)
    $scripter.Options.ScriptSchema=$true
    $scripter.Options.ScriptData=$false
    $scripter.Options.WithDependencies=$false
    $scripter.Options.DriAll=$true
    $scripter.Options.Indexes=$true
    $scripter.Options.Triggers=$true
    $scripter.Options.AnsiPadding=$true
    $scripter.Options.IncludeDatabaseContext=$false
    $scripter.Options.ExtendedProperties=$true
    $scripter.Options.TargetServerVersion=[Microsoft.SqlServer.Management.Smo.SqlServerVersion]::Version160
    $urns=@($database.Tables | Where-Object {!$_.IsSystemObject} | ForEach-Object {$_.Urn})
    $writer=[IO.StreamWriter]::new($temporaryOutput,$false,[Text.UTF8Encoding]::new($true))
    try {
        $writer.WriteLine('-- NexusFreelance: current schema and synthetic demonstration data only. UTF-8.')
        $writer.WriteLine('-- Never drop or replace an existing database. SQL Server 2022 or newer; use sqlcmd -b.')
        $writer.WriteLine('-- Demo funds only. Never connect real payments. Target database name appears in the following header.')
        $writer.WriteLine('USE [master];')
        $writer.WriteLine('GO')
        $writer.WriteLine("IF DB_ID(N'$TargetDatabase') IS NOT NULL BEGIN RAISERROR('Database already exists. Import cancelled.',16,1); SET NOEXEC ON; END;")
        $writer.WriteLine('GO')
        $writer.WriteLine("CREATE DATABASE [$TargetDatabase];")
        $writer.WriteLine('GO')
        $writer.WriteLine("ALTER DATABASE [$TargetDatabase] SET COMPATIBILITY_LEVEL=160;")
        $writer.WriteLine('GO')
        $writer.WriteLine("USE [$TargetDatabase];")
        $writer.WriteLine('GO')
        foreach($batch in $scripter.EnumScript([Microsoft.SqlServer.Management.Sdk.Sfc.Urn[]]$urns)) {
            $writer.WriteLine($batch)
            $writer.WriteLine('GO')
        }
        # SQL Server 2025 dependency discovery can return DBNull in SMO. Schema
        # and row batches are scripted separately; validate all FKs after import.
        $dataTables=if($SchemaOnly) { @($database.Tables | Where-Object Name -eq '__MigrationHistory') } else { @($database.Tables | Where-Object {!$_.IsSystemObject}) }
        foreach($table in $database.Tables | Where-Object {!$_.IsSystemObject}) {
            $writer.WriteLine('ALTER TABLE '+$table.ToString()+' NOCHECK CONSTRAINT ALL;')
            $writer.WriteLine('DISABLE TRIGGER ALL ON '+$table.ToString()+';')
            $writer.WriteLine('GO')
        }
        $scripter.Options.ScriptSchema=$false
        $scripter.Options.ScriptData=$true
        foreach($table in $dataTables) {
            foreach($batch in $scripter.EnumScript([Microsoft.SqlServer.Management.Sdk.Sfc.Urn[]]@($table.Urn))) {
                $writer.WriteLine($batch); $writer.WriteLine('GO')
            }
        }
        foreach($table in $database.Tables | Where-Object {!$_.IsSystemObject}) {
            $writer.WriteLine('ALTER TABLE '+$table.ToString()+' WITH CHECK CHECK CONSTRAINT ALL;')
            $writer.WriteLine('ENABLE TRIGGER ALL ON '+$table.ToString()+';')
            $writer.WriteLine('GO')
        }
    } finally {$writer.Dispose()}
    Move-Item -LiteralPath $temporaryOutput -Destination $absoluteOutput -Force
    'SQL_EXPORT_COMPLETE bytes='+(Get-Item -LiteralPath $OutputPath).Length
} catch {
    for($failure=$_.Exception;$null -ne $failure;$failure=$failure.InnerException) {
        Write-Host ($failure.GetType().Name+': '+$failure.Message)
    }
    throw 'Full SQL export did not complete.'
} finally {
    $server.ConnectionContext.Disconnect()
    if(Test-Path -LiteralPath $temporaryOutput) { Remove-Item -LiteralPath $temporaryOutput -Force }
}
