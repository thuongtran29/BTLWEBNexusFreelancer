-- READ-ONLY invariant audit for the isolated NexusFreelanceMvp database.
-- Run: SQLCMD.EXE -S .\SQLEXPRESS -d NexusFreelanceMvp -E -C -b -i tools\SqlAudit.sql
SET NOCOUNT ON;
IF DB_NAME() <> 'NexusFreelanceMvp'
    THROW 51090, 'Audit is restricted to NexusFreelanceMvp', 1;

DECLARE @audit TABLE (Metric nvarchar(100), Violations bigint);
INSERT INTO @audit (Metric, Violations)
SELECT 'AssignmentsAboveCapacity', COUNT(*) FROM (
    SELECT r.Id FROM dbo.RoleRequirements r
    LEFT JOIN dbo.RoleAssignments a ON a.RoleRequirementId=r.Id
    GROUP BY r.Id, r.RequiredCount HAVING COUNT(a.Id)>r.RequiredCount
) bad
UNION ALL
SELECT 'AcceptedWithoutAssignment', COUNT(*) FROM dbo.Proposals p
WHERE p.Status='Accepted' AND NOT EXISTS (
    SELECT 1 FROM dbo.RoleAssignments a WHERE a.ProposalId=p.Id)
UNION ALL
SELECT 'WithdrawnWithAssignment', COUNT(*) FROM dbo.Proposals p
WHERE p.Status='Withdrawn' AND EXISTS (
    SELECT 1 FROM dbo.RoleAssignments a WHERE a.ProposalId=p.Id)
UNION ALL
SELECT 'MismatchedAssignmentProjectOrApplicant', COUNT(*) FROM dbo.RoleAssignments a
JOIN dbo.Proposals p ON p.Id=a.ProposalId
JOIN dbo.RoleRequirements r ON r.Id=a.RoleRequirementId
WHERE a.ProjectId<>p.ProjectId OR a.ProjectId<>r.ProjectId
    OR p.RoleRequirementId<>a.RoleRequirementId OR p.ApplicantId<>a.FreelancerId
UNION ALL
SELECT 'DuplicateUserProjectTeam', COUNT(*) FROM (
    SELECT pt.ProjectId, tm.UserAccountId FROM dbo.TeamMembers tm
    JOIN dbo.ProjectTeams pt ON pt.Id=tm.ProjectTeamId
    GROUP BY pt.ProjectId, tm.UserAccountId HAVING COUNT(*)>1
) bad
UNION ALL
SELECT 'TeamMemberWithoutMatchingAssignment', COUNT(*) FROM dbo.TeamMembers tm
JOIN dbo.ProjectTeams pt ON pt.Id=tm.ProjectTeamId
WHERE NOT EXISTS (SELECT 1 FROM dbo.RoleAssignments a WHERE a.Id=tm.RoleAssignmentId
    AND a.ProjectId=pt.ProjectId AND a.FreelancerId=tm.UserAccountId)
UNION ALL
SELECT 'WorkspaceWithoutLockedTeam', COUNT(*) FROM dbo.Workspaces w
JOIN dbo.ProjectTeams pt ON pt.Id=w.ProjectTeamId
WHERE pt.ProjectId<>w.ProjectId OR (w.ContractId IS NULL AND pt.Locked=0)
UNION ALL
-- Older tasks predate TaskHistories; their first recorded event may be a status change.
SELECT 'TaskHistoryInvalidCreationSequence', COUNT(*) FROM dbo.Tasks t
WHERE (SELECT COUNT(*) FROM dbo.TaskHistories h WHERE h.WorkTaskId=t.Id AND h.EventType='Created')>1
   OR EXISTS (SELECT 1 FROM dbo.TaskHistories h WHERE h.WorkTaskId=t.Id AND h.EventType='Created'
       AND h.Id<>(SELECT MIN(firstEvent.Id) FROM dbo.TaskHistories firstEvent WHERE firstEvent.WorkTaskId=t.Id))
UNION ALL
SELECT 'TaskHistoryLatestStateMismatch', COUNT(*) FROM dbo.Tasks t
WHERE EXISTS (SELECT 1 FROM dbo.TaskHistories h WHERE h.WorkTaskId=t.Id)
AND t.Status<>(SELECT TOP (1) h.NewStatus FROM dbo.TaskHistories h
    WHERE h.WorkTaskId=t.Id ORDER BY h.Id DESC)
UNION ALL
SELECT 'TaskHistoryBadActorOrTransition', COUNT(*) FROM dbo.TaskHistories h
JOIN dbo.Tasks t ON t.Id=h.WorkTaskId
JOIN dbo.Workspaces w ON w.Id=t.WorkspaceId
JOIN dbo.Projects p ON p.Id=w.ProjectId
LEFT JOIN dbo.Contracts taskContract ON taskContract.Id=w.ContractId
WHERE (h.EventType IN ('Created','Edited') AND
       ((w.ContractId IS NOT NULL AND h.ActorId<>taskContract.CounterpartyId)
        OR (w.ContractId IS NULL AND h.ActorId<>p.ClientId AND NOT EXISTS(
          SELECT 1 FROM dbo.TeamMembers tm WHERE tm.ProjectTeamId=w.ProjectTeamId AND tm.UserAccountId=h.ActorId
          AND (p.ProjectType='SOLO' OR EXISTS(SELECT 1 FROM dbo.GroupBids gb WHERE gb.ProjectId=p.Id AND gb.Status='Accepted' AND gb.LeaderId=h.ActorId))))))
   OR (h.EventType='StatusChanged' AND h.ActorId<>COALESCE(taskContract.CounterpartyId,p.ClientId) AND h.ActorId<>
       COALESCE((SELECT TOP (1) e.OldAssigneeId FROM dbo.TaskHistories e
                 WHERE e.WorkTaskId=h.WorkTaskId AND e.EventType='Edited' AND e.Id>h.Id
                 ORDER BY e.Id),t.AssigneeId))
   OR (h.EventType='Created' AND (h.OldStatus IS NOT NULL OR h.NewStatus<>'ToDo'))
   OR (h.EventType='StatusChanged' AND (h.OldStatus IS NULL OR h.OldStatus=h.NewStatus))
   OR (h.EventType='Edited' AND (h.OldStatus<>h.NewStatus OR h.OldTitle IS NULL
       OR h.NewTitle IS NULL OR h.OldAssigneeId IS NULL OR h.NewAssigneeId IS NULL))
   OR h.EventType NOT IN ('Created','Edited','StatusChanged')
UNION ALL
SELECT 'TaskHistoryLatestContentMismatch', COUNT(*) FROM dbo.Tasks t
CROSS APPLY (SELECT TOP (1) h.NewTitle,h.NewAssigneeId,h.NewPriority,h.NewDueAtUtc
             FROM dbo.TaskHistories h WHERE h.WorkTaskId=t.Id AND h.EventType='Edited'
             ORDER BY h.Id DESC) latest
WHERE t.Title<>latest.NewTitle OR t.AssigneeId<>latest.NewAssigneeId
   OR COALESCE(t.Priority,'Normal')<>COALESCE(latest.NewPriority,'Normal')
   OR (t.DueAtUtc<>latest.NewDueAtUtc OR (t.DueAtUtc IS NULL AND latest.NewDueAtUtc IS NOT NULL)
       OR (t.DueAtUtc IS NOT NULL AND latest.NewDueAtUtc IS NULL))
UNION ALL
SELECT 'CompletedWithoutWorkspace', COUNT(*) FROM dbo.Projects p
WHERE p.Status='Completed' AND NOT EXISTS
    (SELECT 1 FROM dbo.Workspaces w WHERE w.ProjectId=p.Id)
UNION ALL
SELECT 'ReviewOwnershipOrMembership', COUNT(*) FROM dbo.Reviews r
JOIN dbo.Projects p ON p.Id=r.ProjectId
WHERE r.ClientId<>p.ClientId OR r.FreelancerId=r.ClientId
    OR NOT EXISTS (SELECT 1 FROM dbo.RoleAssignments a
                   WHERE a.ProjectId=r.ProjectId AND a.FreelancerId=r.FreelancerId)
UNION ALL
SELECT 'DuplicateReview', COUNT(*) FROM (
    SELECT ProjectId,FreelancerId FROM dbo.Reviews
    GROUP BY ProjectId,FreelancerId HAVING COUNT(*)>1
) bad
UNION ALL
SELECT 'ReviewWithoutContribution', COUNT(*) FROM dbo.Reviews r
WHERE NOT EXISTS (SELECT 1 FROM dbo.ContributionRecords c WHERE c.ReviewId=r.Id)
UNION ALL
SELECT 'ContributionWithoutXPOrSkillDNA', COUNT(*) FROM dbo.ContributionRecords c
WHERE NOT EXISTS (SELECT 1 FROM dbo.XPTransactions x WHERE x.ContributionRecordId=c.Id)
   OR NOT EXISTS (SELECT 1 FROM dbo.SkillDNAHistories h WHERE h.ContributionRecordId=c.Id)
UNION ALL
SELECT 'DuplicateMemoryVersion', COUNT(*) FROM (
    SELECT ProjectId,Version FROM dbo.ProjectMemories
    GROUP BY ProjectId,Version HAVING COUNT(*)>1
) bad
UNION ALL
SELECT 'BadDeliverableRevisionChain', COUNT(*) FROM dbo.Deliverables d
JOIN dbo.Deliverables original ON original.Id=d.RevisesId
WHERE d.WorkspaceId<>original.WorkspaceId OR d.SubmittedById<>original.SubmittedById
UNION ALL
SELECT 'MilestoneInvalidStateOrCompletion', COUNT(*) FROM dbo.ProjectMilestones m
WHERE m.Status NOT IN ('Planned','Completed')
    OR (m.Status='Planned' AND m.CompletedAtUtc IS NOT NULL)
    OR (m.Status='Completed' AND (m.CompletedAtUtc IS NULL OR m.CompletedAtUtc<m.CreatedAtUtc))
UNION ALL
SELECT 'MilestoneWrongWorkspaceProject', COUNT(*) FROM dbo.ProjectMilestones m
JOIN dbo.Workspaces w ON w.Id=m.WorkspaceId
JOIN dbo.ProjectTeams pt ON pt.Id=w.ProjectTeamId
WHERE pt.ProjectId<>w.ProjectId
UNION ALL
SELECT 'MeetingWrongWorkspaceProject', COUNT(*) FROM dbo.WorkspaceMeetings mt
JOIN dbo.Workspaces w ON w.Id=mt.WorkspaceId
JOIN dbo.ProjectTeams pt ON pt.Id=w.ProjectTeamId
WHERE pt.ProjectId<>w.ProjectId
UNION ALL
SELECT 'MeetingInvalidUrl', COUNT(*) FROM dbo.WorkspaceMeetings
WHERE Url NOT LIKE 'https://%' OR LEN(Url)>1000
UNION ALL
SELECT 'AppliedDraftWrongProjectOwner', COUNT(*) FROM dbo.AIJobDrafts d
JOIN dbo.Projects p ON p.Id=d.AppliedProjectId
WHERE d.ClientId<>p.ClientId
UNION ALL
SELECT 'GroupLeaderWithoutActiveMembership', COUNT(*) FROM dbo.FreelancerGroups g
WHERE NOT EXISTS (SELECT 1 FROM dbo.FreelancerGroupMembers m
                  WHERE m.FreelancerGroupId=g.Id AND m.UserAccountId=g.LeaderId AND m.Status='Active')
UNION ALL
SELECT 'GroupMemberNonFreelancer', COUNT(*) FROM dbo.FreelancerGroupMembers m
JOIN dbo.Users u ON u.Id=m.UserAccountId WHERE u.Role<>'Freelancer'
UNION ALL
SELECT 'GroupMemberInvalidDates', COUNT(*) FROM dbo.FreelancerGroupMembers m
WHERE (m.Status='Invited' AND m.RespondedAtUtc IS NOT NULL)
   OR (m.Status<>'Invited' AND m.RespondedAtUtc IS NULL)
   OR (m.RespondedAtUtc IS NOT NULL AND m.RespondedAtUtc<m.InvitedAtUtc)
UNION ALL
SELECT 'GroupBidWithoutProposals', COUNT(*) FROM dbo.GroupBids b
WHERE NOT EXISTS (SELECT 1 FROM dbo.Proposals p WHERE p.GroupBidId=b.Id)
UNION ALL
SELECT 'GroupBidWrongProjectOrRole', COUNT(*) FROM dbo.Proposals p
JOIN dbo.GroupBids b ON b.Id=p.GroupBidId
JOIN dbo.RoleRequirements r ON r.Id=p.RoleRequirementId
WHERE p.ProjectId<>b.ProjectId OR r.ProjectId<>b.ProjectId
UNION ALL
SELECT 'GroupBidInconsistentChildStatus', COUNT(*) FROM dbo.Proposals p
JOIN dbo.GroupBids b ON b.Id=p.GroupBidId
WHERE p.Status<>b.Status
UNION ALL
SELECT 'GroupBidDuplicateCandidate', COUNT(*) FROM (
    SELECT GroupBidId,ApplicantId FROM dbo.Proposals WHERE GroupBidId IS NOT NULL
    GROUP BY GroupBidId,ApplicantId HAVING COUNT(*)>1
) bad
UNION ALL
SELECT 'GroupBidLeaderMismatch', COUNT(*) FROM dbo.GroupBids b
JOIN dbo.FreelancerGroups g ON g.Id=b.FreelancerGroupId
WHERE b.LeaderId<>g.LeaderId
UNION ALL
SELECT 'GroupBidAcceptedWithoutAssignmentAndMembership', COUNT(*) FROM dbo.Proposals p
JOIN dbo.GroupBids b ON b.Id=p.GroupBidId
WHERE b.Status='Accepted' AND
    (NOT EXISTS (SELECT 1 FROM dbo.RoleAssignments a WHERE a.ProposalId=p.Id)
     OR NOT EXISTS (SELECT 1 FROM dbo.RoleAssignments a
         JOIN dbo.TeamMembers tm ON tm.RoleAssignmentId=a.Id
         JOIN dbo.ProjectTeams t ON t.Id=tm.ProjectTeamId
         WHERE a.ProposalId=p.Id AND t.ProjectId=b.ProjectId AND tm.UserAccountId=p.ApplicantId))
UNION ALL
SELECT 'GroupBidInactiveProposalWithAssignment', COUNT(*) FROM dbo.Proposals p
JOIN dbo.GroupBids b ON b.Id=p.GroupBidId
WHERE b.Status IN ('Pending','Shortlisted','Rejected','Withdrawn')
AND EXISTS (SELECT 1 FROM dbo.RoleAssignments a WHERE a.ProposalId=p.Id)
UNION ALL
SELECT 'SuspendedAdministrator', COUNT(*) FROM dbo.Users
WHERE Role='Admin' AND IsSuspended=1
UNION ALL
SELECT 'MalformedAdminUserAudit', COUNT(*) FROM dbo.AdminAuditLogs a
JOIN dbo.Users target ON target.Id=a.TargetUserId
WHERE a.Action IN ('SuspendUser','RestoreUser')
  AND (LEN(LTRIM(RTRIM(a.Reason)))<5
       OR a.ActorId=a.TargetUserId OR target.Role='Admin')
UNION ALL
-- Catalog actions do not target a user; the actor is the required audit anchor.
SELECT 'MalformedAdminCatalogAudit', COUNT(*) FROM dbo.AdminAuditLogs a
WHERE a.Action IN ('CreateSkillTrack','UpdateSkillTrack','CreateExamQuestion','UpdateExamQuestion')
  AND (a.ActorId<>a.TargetUserId OR LEN(LTRIM(RTRIM(a.Reason)))<5
       OR (a.Action IN ('CreateSkillTrack','UpdateSkillTrack') AND a.Reason NOT LIKE 'SkillTrack #%')
       OR (a.Action IN ('CreateExamQuestion','UpdateExamQuestion') AND a.Reason NOT LIKE 'Question #%'))
UNION ALL
SELECT 'ReportInvalidReporterOrProject', COUNT(*) FROM dbo.ProjectAbuseReports r
JOIN dbo.Projects p ON p.Id=r.ProjectId
-- Current role can differ from the role used to submit an older valid report.
WHERE p.ClientId=r.ReporterId
UNION ALL
SELECT 'ReportInvalidCategoryOrContent', COUNT(*) FROM dbo.ProjectAbuseReports r
WHERE r.Category NOT IN ('Fraud','Spam','Abuse','Other')
   OR LEN(LTRIM(RTRIM(r.Details))) NOT BETWEEN 15 AND 2000
   OR r.Status NOT IN ('Pending','Confirmed','Dismissed')
UNION ALL
SELECT 'ReportInvalidReview', COUNT(*) FROM dbo.ProjectAbuseReports r
WHERE (r.Status='Pending' AND (r.ReviewerId IS NOT NULL OR r.ReviewedAtUtc IS NOT NULL OR r.Resolution IS NOT NULL))
   OR (r.Status<>'Pending' AND (r.ReviewerId IS NULL OR r.ReviewedAtUtc IS NULL
       OR r.Resolution IS NULL OR LEN(LTRIM(RTRIM(r.Resolution))) NOT BETWEEN 5 AND 200
       OR r.ReviewedAtUtc<r.CreatedAtUtc))
UNION ALL
SELECT 'ReportDuplicatePending', COUNT(*) FROM (
    SELECT ProjectId,ReporterId FROM dbo.ProjectAbuseReports
    WHERE Status='Pending' GROUP BY ProjectId,ReporterId HAVING COUNT(*)>1
) bad
UNION ALL
SELECT 'ReportReviewWithoutAdminAudit', COUNT(*) FROM dbo.ProjectAbuseReports r
JOIN dbo.Projects p ON p.Id=r.ProjectId
WHERE r.Status<>'Pending' AND NOT EXISTS (
    SELECT 1 FROM dbo.AdminAuditLogs a WHERE a.Action='ReviewProjectReport'
    AND a.ActorId=r.ReviewerId AND a.TargetUserId=p.ClientId
    AND a.Reason=r.Resolution
    AND ABS(DATEDIFF(SECOND,a.CreatedAtUtc,r.ReviewedAtUtc))<=5)
UNION ALL
SELECT 'MalformedReportAdminAudit', COUNT(*) FROM dbo.AdminAuditLogs a
WHERE a.Action='ReviewProjectReport' AND (
    LEN(LTRIM(RTRIM(a.Reason)))<5 OR NOT EXISTS (
        SELECT 1 FROM dbo.ProjectAbuseReports r JOIN dbo.Projects p ON p.Id=r.ProjectId
        WHERE r.ReviewerId=a.ActorId AND p.ClientId=a.TargetUserId
        AND r.Resolution=a.Reason AND r.Status IN ('Confirmed','Dismissed')
        AND ABS(DATEDIFF(SECOND,a.CreatedAtUtc,r.ReviewedAtUtc))<=5))
UNION ALL
SELECT 'MalformedBadgeAdminAudit', COUNT(*) FROM dbo.AdminAuditLogs a
WHERE a.Action='RevokeBadge' AND (
    a.ActorId=a.TargetUserId OR LEN(LTRIM(RTRIM(a.Reason)))<15
    OR a.Reason NOT LIKE 'Badge #[0-9]%: %')
UNION ALL
SELECT 'CertificationInvalidDatesOrEvidence', COUNT(*) FROM dbo.ExternalCertifications c
WHERE c.IssuedOn < CONVERT(date,'19000101') OR c.IssuedOn > CONVERT(date,GETDATE())
    OR (c.ExpiresOn IS NOT NULL AND c.ExpiresOn<=c.IssuedOn)
    OR c.EvidenceUrl NOT LIKE 'https://%'
UNION ALL
SELECT 'CertificationInvalidReview', COUNT(*) FROM dbo.ExternalCertifications c
WHERE (c.Status='Pending' AND (c.ReviewedById IS NOT NULL OR c.ReviewedAtUtc IS NOT NULL OR c.ReviewReason IS NOT NULL))
    OR (c.Status IN ('Verified','Rejected') AND (c.ReviewedById IS NULL OR c.ReviewedAtUtc IS NULL
        OR c.ReviewReason IS NULL OR LEN(LTRIM(RTRIM(c.ReviewReason)))<5
        OR c.ReviewedAtUtc<c.SubmittedAtUtc OR c.ReviewedById=c.UserAccountId))
UNION ALL
SELECT 'CertificationReviewWithoutAudit', COUNT(*) FROM dbo.ExternalCertifications c
WHERE c.Status IN ('Verified','Rejected') AND NOT EXISTS (
    SELECT 1 FROM dbo.AdminAuditLogs a
    WHERE a.ActorId=c.ReviewedById AND a.TargetUserId=c.UserAccountId
    AND a.Action=CASE c.Status WHEN 'Verified' THEN 'VerifyCertification' ELSE 'RejectCertification' END
    AND a.Reason=LEFT(N'Certification #'+CONVERT(nvarchar(12),c.Id)+N': '+c.ReviewReason,200)
    AND ABS(DATEDIFF(SECOND,a.CreatedAtUtc,c.ReviewedAtUtc))<=5)
UNION ALL
SELECT 'MalformedCertificationAdminAudit', COUNT(*) FROM dbo.AdminAuditLogs a
WHERE a.Action IN ('VerifyCertification','RejectCertification') AND NOT EXISTS (
    SELECT 1 FROM dbo.ExternalCertifications c
    WHERE c.ReviewedById=a.ActorId AND c.UserAccountId=a.TargetUserId
    AND c.Status=CASE a.Action WHEN 'VerifyCertification' THEN 'Verified' ELSE 'Rejected' END
    AND a.Reason=LEFT(N'Certification #'+CONVERT(nvarchar(12),c.Id)+N': '+c.ReviewReason,200)
    AND ABS(DATEDIFF(SECOND,a.CreatedAtUtc,c.ReviewedAtUtc))<=5)
UNION ALL
SELECT 'UnknownAdminAuditAction', COUNT(*) FROM dbo.AdminAuditLogs a
WHERE a.Action NOT IN ('SuspendUser','RestoreUser','CreateSkillTrack','UpdateSkillTrack',
                       'CreateExamQuestion','UpdateExamQuestion','ReviewProjectReport','RevokeBadge',
                       'VerifyCertification','RejectCertification','ReviewMoneyRequest','ResolveEscrowDispute','ReviewSkillEvidence','RequestPspRefund','ReconcilePspRefund')
UNION ALL
SELECT 'DisabledOrUntrustedForeignKey', COUNT(*) FROM sys.foreign_keys
WHERE is_disabled=1 OR is_not_trusted=1
UNION ALL
SELECT 'DisabledOrUntrustedCheck', COUNT(*) FROM sys.check_constraints
WHERE is_disabled=1 OR is_not_trusted=1;

SELECT Metric, Violations FROM @audit ORDER BY Metric;
SELECT
    (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0) AS UserTables,
    (SELECT COUNT(*) FROM sys.foreign_keys) AS ForeignKeys,
    (SELECT COUNT(*) FROM sys.check_constraints) AS CheckConstraints,
    (SELECT COUNT(*) FROM dbo.__MigrationHistory) AS MigrationEntries;

IF EXISTS (SELECT 1 FROM @audit WHERE Violations<>0)
    THROW 51091, 'NexusFreelanceMvp audit found invariant violations', 1;
PRINT 'SQL_AUDIT_PASSED';
