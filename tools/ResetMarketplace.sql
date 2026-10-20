-- Destructive development reset explicitly requested by the project owner.
-- Requires a recent full/copy-only backup. Keeps users, profiles, skills, badges and certificates.
-- Run with SQLCMD -I -b -v ConfirmReset=NEXUSFREELANCE_RESET_PROJECTS_AND_TEAMS -i tools\ResetMarketplace.sql
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
IF DB_NAME()<>'NexusFreelanceMvp' THROW 51200,'Reset restricted to development database NexusFreelanceMvp',1;
IF '$(ConfirmReset)'<>'NEXUSFREELANCE_RESET_PROJECTS_AND_TEAMS' THROW 51201,'Explicit reset token required',1;
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset WHERE database_name=DB_NAME() AND type='D' AND backup_finish_date>DATEADD(hour,-24,GETDATE()))
    THROW 51202,'A recent database backup is required before reset',1;
-- These finance tables were introduced in v3. Only known disposable test
-- accounts may be cleared; refuse if a different user's financial data exists.
IF EXISTS(SELECT 1 FROM dbo.WalletAccounts w JOIN dbo.Users u ON u.Id=w.UserAccountId WHERE u.Email NOT LIKE 'v3-%@example.test' AND u.Email NOT LIKE 'contractv2-%@example.test')
OR EXISTS(SELECT 1 FROM dbo.MoneyRequests r JOIN dbo.Users u ON u.Id=r.UserAccountId WHERE u.Email NOT LIKE 'v3-%@example.test' AND u.Email NOT LIKE 'contractv2-%@example.test')
OR EXISTS(SELECT 1 FROM dbo.WalletEntries e JOIN dbo.Users u ON u.Id=e.UserAccountId WHERE u.Email NOT LIKE 'v3-%@example.test' AND u.Email NOT LIKE 'contractv2-%@example.test')
    THROW 51203,'Non-fixture financial records detected; do not erase financial history',1;
SELECT (SELECT COUNT(*) FROM dbo.Projects) ProjectsBefore,(SELECT COUNT(*) FROM dbo.FreelancerGroups) GroupsBefore,
       (SELECT COUNT(*) FROM dbo.Users) UsersPreserved;
BEGIN TRANSACTION;
BEGIN TRY
    -- These audit actions belong to the project/finance records being removed.
    -- Account, skill, badge and certificate moderation audits remain intact.
    DELETE dbo.AdminAuditLogs WHERE Action IN('ReviewProjectReport','ReviewMoneyRequest','ResolveEscrowDispute','RequestPspRefund','ReconcilePspRefund');
    DELETE dbo.Notifications WHERE TargetPath LIKE '/Projects/%' OR TargetPath LIKE '/Groups/%'
        OR TargetPath LIKE '/Workspace/%' OR TargetPath LIKE '/Proposals/%'
        OR TargetPath LIKE '/Negotiation/%' OR TargetPath LIKE '/GroupBids/%' OR TargetPath LIKE '/Wallet%'
        OR TargetPath LIKE '/Contracts%' OR TargetPath LIKE '/Payments%';
    -- Keep portfolio contents; detach only the project association being reset.
    UPDATE dbo.PortfolioItems SET ProjectId=NULL WHERE ProjectId IS NOT NULL;
    CREATE TABLE #targets(id int PRIMARY KEY);
    INSERT #targets VALUES(OBJECT_ID('dbo.Projects')),(OBJECT_ID('dbo.FreelancerGroups')),(OBJECT_ID('dbo.AIJobDrafts')),
        (OBJECT_ID('dbo.WalletEntries')),(OBJECT_ID('dbo.WalletAccounts')),(OBJECT_ID('dbo.MoneyRequests'));
    WHILE 1=1
    BEGIN
        INSERT #targets SELECT DISTINCT fk.parent_object_id FROM sys.foreign_keys fk
        JOIN #targets t ON t.id=fk.referenced_object_id
        WHERE NOT EXISTS(SELECT 1 FROM #targets x WHERE x.id=fk.parent_object_id)
        AND fk.parent_object_id<>OBJECT_ID('dbo.PortfolioItems');
        IF @@ROWCOUNT=0 BREAK;
    END;
    IF EXISTS(SELECT 1 FROM #targets WHERE id IN(OBJECT_ID('dbo.Users'),OBJECT_ID('dbo.FreelancerProfiles'),OBJECT_ID('dbo.ClientProfiles'),OBJECT_ID('dbo.SkillTracks')))
        THROW 51204,'Unexpected dependency touches preserved data',1;
    DECLARE @id int,@table nvarchar(300),@sql nvarchar(max);
    WHILE EXISTS(SELECT 1 FROM #targets)
    BEGIN
        SET @id=NULL;
        SELECT TOP(1) @id=t.id FROM #targets t WHERE NOT EXISTS(
            SELECT 1 FROM sys.foreign_keys fk JOIN #targets child ON child.id=fk.parent_object_id
            WHERE fk.referenced_object_id=t.id AND fk.parent_object_id<>t.id) ORDER BY t.id;
        IF @id IS NULL THROW 51205,'Cyclic dependency: reset rolled back',1;
        SET @table=QUOTENAME(OBJECT_SCHEMA_NAME(@id))+'.'+QUOTENAME(OBJECT_NAME(@id));
        SET @sql=N'DELETE FROM '+@table+N';'; EXEC sp_executesql @sql;
        DELETE #targets WHERE id=@id;
    END;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;
SELECT (SELECT COUNT(*) FROM dbo.Projects) ProjectsAfter,(SELECT COUNT(*) FROM dbo.FreelancerGroups) GroupsAfter,
       (SELECT COUNT(*) FROM dbo.Users) UsersPreserved,(SELECT COUNT(*) FROM dbo.WalletEntries) WalletEntriesAfter;
PRINT 'MARKETPLACE_RESET_PASSED';
