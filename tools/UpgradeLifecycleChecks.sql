-- One-time or repeatable schema update, restricted to the isolated test database.
-- sqlcmd -S .\SQLEXPRESS -d NexusFreelanceMvp -E -C -b -i tools\UpgradeLifecycleChecks.sql
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'NexusFreelanceMvp'
    THROW 51120, 'Lifecycle upgrade is allowed only on NexusFreelanceMvp', 1;

BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = 'CK_Project_Status' AND parent_object_id = OBJECT_ID('dbo.Projects')
             AND (definition NOT LIKE '%''Paused''%' OR definition NOT LIKE '%''Cancelled''%'))
    ALTER TABLE dbo.Projects DROP CONSTRAINT CK_Project_Status;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_Project_Status' AND parent_object_id = OBJECT_ID('dbo.Projects'))
    ALTER TABLE dbo.Projects WITH CHECK ADD CONSTRAINT CK_Project_Status
        CHECK ([Status] IN ('Draft','Published','Paused','Cancelled','InProgress','Completed'));

IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = 'CK_Proposal_Status' AND parent_object_id = OBJECT_ID('dbo.Proposals')
             AND (definition NOT LIKE '%''Withdrawn''%' OR definition NOT LIKE '%''Rejected''%'))
    ALTER TABLE dbo.Proposals DROP CONSTRAINT CK_Proposal_Status;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_Proposal_Status' AND parent_object_id = OBJECT_ID('dbo.Proposals'))
    ALTER TABLE dbo.Proposals WITH CHECK ADD CONSTRAINT CK_Proposal_Status
        CHECK ([Status] IN ('Pending','Accepted','Withdrawn','Rejected'));

IF (SELECT COUNT(*) FROM sys.check_constraints
    WHERE (name = 'CK_Project_Status' AND parent_object_id = OBJECT_ID('dbo.Projects')
           AND definition LIKE '%''Paused''%' AND definition LIKE '%''Cancelled''%')
       OR (name = 'CK_Proposal_Status' AND parent_object_id = OBJECT_ID('dbo.Proposals')
           AND definition LIKE '%''Withdrawn''%' AND definition LIKE '%''Rejected''%')) <> 2
    THROW 51121, 'Lifecycle constraints were not upgraded', 1;
COMMIT TRANSACTION;
PRINT 'LIFECYCLE_CONSTRAINTS_UPGRADED';
