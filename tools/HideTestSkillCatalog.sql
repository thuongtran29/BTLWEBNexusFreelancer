-- Hide only recognizable historical E2E/timer catalog fixtures.
-- Keeps questions, exam attempts, badges and evidence history intact.
SET NOCOUNT ON; SET XACT_ABORT ON;
IF DB_NAME()<>'NexusFreelanceMvp' THROW 51510,'Development database required',1;
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset WHERE database_name=DB_NAME() AND type='D' AND backup_finish_date>DATEADD(hour,-24,GETDATE())) THROW 51511,'Recent backup required',1;
BEGIN TRANSACTION;
UPDATE dbo.SkillTracks SET IsActive=0
WHERE (IsActive IS NULL OR IsActive=1) AND
    ((Code LIKE 'E2E[_]%' AND Title LIKE 'Admin Skills E2E %') OR
     (Code LIKE 'TIMER[_]%' AND Title LIKE 'Timer test %') OR
     (LEN(Code)=16 AND Code LIKE 'V3E[_]%' AND SUBSTRING(Code,15,1)='_' AND SUBSTRING(Code,5,10) NOT LIKE '%[^0-9A-F]%'
      AND RIGHT(Code,1) IN('0','1','2') AND Title=N'V3 proof '+LOWER(SUBSTRING(Code,5,10))+N' '+RIGHT(Code,1)));
SELECT @@ROWCOUNT HiddenFixtureTracks;
COMMIT;
SELECT Id,Code,Title FROM dbo.SkillTracks WHERE IsActive IS NULL OR IsActive=1 ORDER BY Id;
