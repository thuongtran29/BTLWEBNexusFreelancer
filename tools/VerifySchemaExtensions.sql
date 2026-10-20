-- Read-only verification of the October 2026 NexusFreelance development schema.
-- Run with sqlcmd -S .\SQLEXPRESS -d NexusFreelanceMvp -E -C -b -i tools\VerifySchemaExtensions.sql
SET NOCOUNT ON;
IF DB_NAME() <> N'NexusFreelanceMvp'
    THROW 51200, 'Wrong target database; only the development NexusFreelanceMvp is supported.', 1;

IF OBJECT_ID(N'dbo.ProjectAbuseReports', N'U') IS NULL
    THROW 51201, 'Missing dbo.ProjectAbuseReports: EF schema migration not applied.', 1;
IF OBJECT_ID(N'dbo.TaskHistories', N'U') IS NULL
    THROW 51202, 'Missing dbo.TaskHistories.', 1;
IF OBJECT_ID(N'dbo.PasswordResetTokens', N'U') IS NULL
    THROW 51208, 'Missing dbo.PasswordResetTokens: EF schema migration not applied.', 1;
IF OBJECT_ID(N'dbo.EmailVerificationTokens', N'U') IS NULL
    THROW 51212, 'Missing dbo.EmailVerificationTokens: EF schema migration not applied.', 1;
IF OBJECT_ID(N'dbo.WorkspaceFiles', N'U') IS NULL OR OBJECT_ID(N'dbo.WorkspaceFileVersions', N'U') IS NULL
    THROW 51213, 'Missing workspace file tables: EF schema migration not applied.', 1;

DECLARE @required TABLE (TableName sysname NOT NULL, ColumnName sysname NOT NULL);
INSERT INTO @required (TableName,ColumnName) VALUES
    (N'ProjectAbuseReports',N'Id'),
    (N'ProjectAbuseReports',N'ProjectId'),
    (N'ProjectAbuseReports',N'ReporterId'),
    (N'ProjectAbuseReports',N'ReviewerId'),
    (N'ProjectAbuseReports',N'Category'),
    (N'ProjectAbuseReports',N'Details'),
    (N'ProjectAbuseReports',N'Status'),
    (N'ProjectAbuseReports',N'CreatedAtUtc'),
    (N'ProjectAbuseReports',N'ReviewedAtUtc'),
    (N'ProjectAbuseReports',N'Resolution'),
    (N'TaskHistories',N'OldTitle'),
    (N'TaskHistories',N'NewTitle'),
    (N'TaskHistories',N'OldAssigneeId'),
    (N'TaskHistories',N'NewAssigneeId'),
    (N'TaskHistories',N'OldPriority'),
    (N'TaskHistories',N'NewPriority'),
    (N'TaskHistories',N'OldDueAtUtc'),
    (N'TaskHistories',N'NewDueAtUtc'),
    (N'Users',N'PasswordChangedAtUtc'),
    (N'Users',N'EmailVerifiedAtUtc'),
    (N'PasswordResetTokens',N'Id'),
    (N'PasswordResetTokens',N'UserAccountId'),
    (N'PasswordResetTokens',N'TokenHash'),
    (N'PasswordResetTokens',N'CreatedAtUtc'),
    (N'PasswordResetTokens',N'ExpiresAtUtc'),
    (N'PasswordResetTokens',N'ConsumedAtUtc'),
    (N'EmailVerificationTokens',N'UserAccountId'),
    (N'EmailVerificationTokens',N'TokenHash'),
    (N'EmailVerificationTokens',N'CreatedAtUtc'),
    (N'EmailVerificationTokens',N'ExpiresAtUtc'),
    (N'EmailVerificationTokens',N'ConsumedAtUtc'),
    (N'WorkspaceFiles',N'WorkspaceId'),
    (N'WorkspaceFiles',N'CreatedById'),
    (N'WorkspaceFileVersions',N'WorkspaceFileId'),
    (N'WorkspaceFileVersions',N'VersionNumber'),
    (N'WorkspaceFileVersions',N'UploadedById'),
    (N'WorkspaceFileVersions',N'FileName'),
    (N'WorkspaceFileVersions',N'ContentType'),
    (N'WorkspaceFileVersions',N'ByteLength'),
    (N'WorkspaceFileVersions',N'Sha256'),
    (N'WorkspaceFileVersions',N'Data'),
    (N'EarnedSkillBadges',N'RevokedAtUtc'),
    (N'EarnedSkillBadges',N'RevokedById'),
    (N'EarnedSkillBadges',N'RevocationReason');

IF EXISTS (
    SELECT 1 FROM @required AS expected
    LEFT JOIN sys.tables AS t ON t.name=expected.TableName AND SCHEMA_NAME(t.schema_id)=N'dbo'
    LEFT JOIN sys.columns AS c ON c.object_id=t.object_id AND c.name=expected.ColumnName
    WHERE c.column_id IS NULL
)
    THROW 51203, 'Required report or Task edit history column missing.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.ProjectAbuseReports')
    AND name=N'UX_ProjectAbuseReports_OnePending'
    AND is_unique=1 AND has_filter=1 AND is_disabled=0
    AND filter_definition LIKE N'%Pending%'
)
    THROW 51204, 'Unique filtered Pending report index missing or disabled.', 1;

IF (
    SELECT COUNT(*) FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.ProjectAbuseReports')
      AND is_disabled=0 AND is_not_trusted=0
) < 3
    THROW 51205, 'Report foreign keys missing, disabled, or untrusted.', 1;

IF (
    SELECT COUNT(*) FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.ProjectAbuseReports')
    AND name IN (N'CK_ProjectAbuseReport_Status',N'CK_ProjectAbuseReport_Category',
                 N'CK_ProjectAbuseReport_Review')
    AND is_disabled=0 AND is_not_trusted=0
) <> 3
    THROW 51206, 'Report validation CHECK constraints missing or untrusted.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.TaskHistories')
      AND name=N'CK_TaskHistory_Event' AND is_disabled=0 AND is_not_trusted=0
      AND definition LIKE N'%Edited%'
)
    THROW 51207, 'Task history CHECK not updated for Edited event.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    JOIN sys.index_columns AS ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    JOIN sys.columns AS c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'dbo.PasswordResetTokens') AND i.is_unique=1
      AND i.is_disabled=0 AND c.name=N'TokenHash' AND ic.key_ordinal=1
)
    THROW 51209, 'Unique token digest index missing.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.PasswordResetTokens')
      AND referenced_object_id=OBJECT_ID(N'dbo.Users')
      AND is_disabled=0 AND is_not_trusted=0
)
    THROW 51210, 'Password reset user FK missing or untrusted.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.PasswordResetTokens')
      AND name=N'CK_PasswordResetToken_Times'
      AND is_disabled=0 AND is_not_trusted=0
)
    THROW 51211, 'Password reset lifetime CHECK missing or untrusted.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    JOIN sys.index_columns AS ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    JOIN sys.columns AS c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'dbo.EmailVerificationTokens') AND i.is_unique=1
      AND i.is_disabled=0 AND c.name=N'TokenHash' AND ic.key_ordinal=1
)
    THROW 51214, 'Verification token digest uniqueness missing.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.EmailVerificationTokens')
      AND referenced_object_id=OBJECT_ID(N'dbo.Users')
      AND is_disabled=0 AND is_not_trusted=0
)
    THROW 51215, 'Verification token user FK missing or untrusted.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.EmailVerificationTokens')
      AND name=N'CK_EmailVerificationToken_Times'
      AND is_disabled=0 AND is_not_trusted=0
)
    THROW 51216, 'Verification token validity CHECK missing.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    JOIN sys.index_columns ic1 ON ic1.object_id=i.object_id AND ic1.index_id=i.index_id AND ic1.key_ordinal=1
    JOIN sys.columns c1 ON c1.object_id=ic1.object_id AND c1.column_id=ic1.column_id
    JOIN sys.index_columns ic2 ON ic2.object_id=i.object_id AND ic2.index_id=i.index_id AND ic2.key_ordinal=2
    JOIN sys.columns c2 ON c2.object_id=ic2.object_id AND c2.column_id=ic2.column_id
    WHERE i.object_id=OBJECT_ID(N'dbo.WorkspaceFileVersions') AND i.is_unique=1
      AND i.is_disabled=0 AND c1.name=N'WorkspaceFileId' AND c2.name=N'VersionNumber'
)
    THROW 51217, 'Workspace file version uniqueness missing.', 1;

IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.WorkspaceFiles')
    AND is_disabled=0 AND is_not_trusted=0) < 2
    THROW 51218, 'WorkspaceFiles FKs missing or untrusted.', 1;
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.WorkspaceFileVersions')
    AND is_disabled=0 AND is_not_trusted=0) < 2
    THROW 51219, 'WorkspaceFileVersions FKs missing or untrusted.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.WorkspaceFileVersions')
      AND name=N'CK_WorkspaceFileVersion_Fields' AND is_disabled=0 AND is_not_trusted=0
)
    THROW 51220, 'Workspace file byte length CHECK missing.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.EarnedSkillBadges')
      AND name=N'CK_EarnedSkillBadge_Revocation'
      AND is_disabled=0 AND is_not_trusted=0
)
    THROW 51221, 'Badge revocation consistency CHECK missing or untrusted.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.EarnedSkillBadges')
      AND referenced_object_id=OBJECT_ID(N'dbo.Users')
      AND name LIKE N'%RevokedById%' AND is_disabled=0 AND is_not_trusted=0
)
    THROW 51222, 'Badge reviewer foreign key missing or untrusted.', 1;

IF EXISTS (
    SELECT 1 FROM dbo.EarnedSkillBadges
    WHERE (RevokedAtUtc IS NULL AND (RevokedById IS NOT NULL OR RevocationReason IS NOT NULL))
       OR (RevokedAtUtc IS NOT NULL AND (RevokedById IS NULL OR RevocationReason IS NULL))
)
    THROW 51223, 'Invalid stored badge revocation record.', 1;

SELECT 'EXTENSION_SCHEMA_VERIFIED' AS Result;
