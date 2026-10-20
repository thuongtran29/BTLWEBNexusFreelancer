-- Read-only verification, suitable after importing the submission SQL.
SET NOCOUNT ON;
IF DB_NAME() NOT LIKE N'NexusFreelanceSubmission%'
 THROW 52011,'Use the isolated submission database only',1;
IF (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0)<>75
 THROW 52012,'Unexpected table count',1;
IF (SELECT COUNT(*) FROM sys.foreign_keys)<>149 OR (SELECT COUNT(*) FROM sys.check_constraints)<>62
 THROW 52019,'Unexpected FK or CHECK inventory',1;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1)
 THROW 52013,'Foreign keys must all be enabled and trusted',1;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1)
 THROW 52014,'CHECK constraints must all be enabled and trusted',1;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE parent_class=1 AND is_disabled=1)
 THROW 52015,'Table triggers must be enabled',1;
IF NOT EXISTS(SELECT 1 FROM dbo.__MigrationHistory)
 THROW 52016,'Missing EF migration metadata',1;
IF EXISTS(SELECT 1 FROM dbo.Users WHERE Email NOT LIKE '%@nexus.example.test' OR GoogleSubject IS NOT NULL)
 THROW 52017,'Unexpected personal identity in submission data',1;
IF EXISTS(SELECT 1 FROM dbo.RegistrationCodes) OR EXISTS(SELECT 1 FROM dbo.PasswordResetTokens)
 THROW 52018,'Submission must not contain OTP or reset tokens',1;
IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Projects') AND name='PublishedAtUtc')
 THROW 52020,'Missing publication timestamp',1;
DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS;
SELECT 'Users' AS Metric,COUNT(*) AS Records FROM dbo.Users
UNION ALL SELECT 'Projects',COUNT(*) FROM dbo.Projects
UNION ALL SELECT 'Contracts',COUNT(*) FROM dbo.Contracts
UNION ALL SELECT 'Groups',COUNT(*) FROM dbo.FreelancerGroups;
PRINT 'SUBMISSION_SCHEMA_AND_DATA_VERIFIED';
