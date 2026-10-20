-- Remove only catalog/evidence fixtures owned by V3SpecFlowE2E.
-- Preserves pre-existing skills, certificates, users and all non-fixture claims.
SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;
IF DB_NAME()<>'NexusFreelanceMvp' THROW 51500,'Development database required',1;
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset WHERE database_name=DB_NAME() AND type='D' AND backup_finish_date>DATEADD(hour,-24,GETDATE())) THROW 51501,'Recent backup required',1;
SELECT Id INTO #skills FROM dbo.SkillTracks WHERE Code LIKE 'V3E[_]%';
IF EXISTS(SELECT 1 FROM dbo.SkillClaims c JOIN #skills s ON s.Id=c.SkillTrackId JOIN dbo.Users u ON u.Id=c.UserAccountId WHERE u.Email NOT LIKE 'v3-%@example.test')
OR EXISTS(SELECT 1 FROM dbo.SkillExamAttempts a JOIN #skills s ON s.Id=a.SkillTrackId JOIN dbo.Users u ON u.Id=a.UserAccountId WHERE u.Email NOT LIKE 'v3-%@example.test')
    THROW 51502,'Non-fixture use detected; preserve this catalog',1;
BEGIN TRANSACTION;
BEGIN TRY
    SELECT r.Id INTO #requests FROM dbo.SkillEvidenceRequests r JOIN dbo.SkillClaims c ON c.Id=r.SkillClaimId JOIN #skills s ON s.Id=c.SkillTrackId;
    DELETE a FROM dbo.AdminAuditLogs a JOIN #requests r ON a.Reason LIKE 'SkillRequest #'+CONVERT(varchar(20),r.Id)+':%' WHERE a.Action='ReviewSkillEvidence';
    DELETE dbo.AdminAuditLogs WHERE Action IN('CreateSkillTrack','UpdateSkillTrack') AND Reason LIKE '%V3E[_]%';
    DELETE e FROM dbo.SkillEvidence e JOIN #requests r ON r.Id=e.SkillEvidenceRequestId;
    DELETE r FROM dbo.SkillEvidenceRequests r JOIN #requests x ON x.Id=r.Id;
    DELETE c FROM dbo.SkillClaims c JOIN #skills s ON s.Id=c.SkillTrackId;
    DELETE b FROM dbo.EarnedSkillBadges b JOIN #skills s ON s.Id=b.SkillTrackId;
    DELETE a FROM dbo.SkillExamAnswers a JOIN dbo.SkillExamAttempts e ON e.Id=a.SkillExamAttemptId JOIN #skills s ON s.Id=e.SkillTrackId;
    DELETE a FROM dbo.SkillExamAttempts a JOIN #skills s ON s.Id=a.SkillTrackId;
    DELETE a FROM dbo.SkillExamSessions a JOIN #skills s ON s.Id=a.SkillTrackId;
    DELETE q FROM dbo.SkillExamQuestions q JOIN #skills s ON s.Id=q.SkillTrackId;
    DELETE t FROM dbo.SkillTracks t JOIN #skills s ON s.Id=t.Id;
    DELETE n FROM dbo.Notifications n JOIN dbo.Users u ON u.Id=n.RecipientId WHERE n.TargetPath LIKE '/SkillEvidence%' AND u.Email LIKE 'v3-%@example.test';
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;
SELECT COUNT(*) RemovedFixtureTracks FROM #skills;
PRINT 'V3_SKILL_FIXTURES_CLEANED';
