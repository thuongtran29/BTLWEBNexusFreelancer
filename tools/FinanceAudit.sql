-- Read-only v3 accounting and role invariants. Does not move or modify money.
SET NOCOUNT ON;
IF DB_NAME()<>'NexusFreelanceMvp' THROW 51100,'Development database required',1;
DECLARE @a TABLE(Metric nvarchar(100), Violations bigint);
INSERT @a
SELECT 'DuplicateEmailRole',COUNT(*) FROM (SELECT Email,Role FROM dbo.Users GROUP BY Email,Role HAVING COUNT(*)>1) d
UNION ALL SELECT 'WalletLedgerMismatch',COUNT(*) FROM dbo.WalletAccounts w
WHERE w.Balance<>COALESCE((SELECT SUM(e.BalanceDelta) FROM dbo.WalletEntries e WHERE e.UserAccountId=w.UserAccountId),0)
OR w.Held<>COALESCE((SELECT SUM(e.HeldDelta) FROM dbo.WalletEntries e WHERE e.UserAccountId=w.UserAccountId),0)
UNION ALL SELECT 'HeldEscrowMismatch',COUNT(*) FROM dbo.WalletAccounts w
WHERE w.Held<>COALESCE((SELECT SUM(m.Amount) FROM dbo.EscrowMilestones m JOIN dbo.Projects p ON p.Id=m.ProjectId
WHERE p.ClientId=w.UserAccountId AND m.Status IN('Funded','Submitted','RevisionsRequested')),0)
UNION ALL SELECT 'NegativeWallet',COUNT(*) FROM dbo.WalletAccounts WHERE Balance<0 OR Held<0
UNION ALL SELECT 'EscrowPlanTotalMismatch',COUNT(*) FROM dbo.Projects p WHERE EXISTS(SELECT 1 FROM dbo.EscrowMilestones m WHERE m.ProjectId=p.Id)
AND NOT EXISTS(SELECT 1 FROM dbo.Contracts c WHERE c.ProjectId=p.Id)
AND p.Budget<>(SELECT SUM(m.Amount) FROM dbo.EscrowMilestones m WHERE m.ProjectId=p.Id)
UNION ALL SELECT 'EscrowShareTotalMismatch',COUNT(*) FROM dbo.Projects p WHERE EXISTS(SELECT 1 FROM dbo.EscrowShares s WHERE s.ProjectId=p.Id)
AND NOT EXISTS(SELECT 1 FROM dbo.Contracts c WHERE c.ProjectId=p.Id)
AND p.Budget<>(SELECT SUM(s.Weight) FROM dbo.EscrowShares s WHERE s.ProjectId=p.Id)
UNION ALL SELECT 'EscrowShareNotAssigned',COUNT(*) FROM dbo.EscrowShares s WHERE
(s.ContractId IS NULL AND NOT EXISTS(SELECT 1 FROM dbo.RoleAssignments a WHERE a.ProjectId=s.ProjectId AND a.FreelancerId=s.UserAccountId))
OR (s.ContractId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.ContractMembers cm WHERE cm.ContractId=s.ContractId AND cm.UserAccountId=s.UserAccountId AND cm.Weight=s.Weight))
UNION ALL SELECT 'SettledMilestoneConservation',COUNT(*) FROM dbo.EscrowMilestones m JOIN dbo.Projects p ON p.Id=m.ProjectId
WHERE m.Status IN('Released','Refunded') AND (m.Fee<>FLOOR((m.Amount-m.Refunded)*COALESCE((SELECT v.FeeRate FROM dbo.Contracts c JOIN dbo.ContractVersions v ON v.ContractId=c.Id AND v.VersionNumber=c.CurrentVersion WHERE c.Id=m.ContractId),p.PlatformFeeRate))
OR m.Amount<>m.Fee+m.Refunded+COALESCE((SELECT SUM(e.Amount) FROM dbo.WalletEntries e WHERE e.EscrowMilestoneId=m.Id AND e.Kind='Payout'),0))
UNION ALL SELECT 'EscrowMissingHoldOrSettlement',COUNT(*) FROM dbo.EscrowMilestones m
WHERE (m.Status<>'Planned' AND (SELECT COUNT(*) FROM dbo.WalletEntries e WHERE e.EscrowMilestoneId=m.Id AND e.Kind='EscrowHold')<>1)
OR (m.Status IN('Released','Refunded') AND (SELECT COUNT(*) FROM dbo.WalletEntries e WHERE e.EscrowMilestoneId=m.Id AND e.Kind='EscrowSettle')<>1)
UNION ALL SELECT 'MoneyRequestJournalMismatch',COUNT(*) FROM dbo.MoneyRequests r
WHERE (r.Status='Approved' AND NOT EXISTS(SELECT 1 FROM dbo.WalletEntries e WHERE e.Reference='money:'+CONVERT(varchar(20),r.Id)
AND e.UserAccountId=r.UserAccountId AND e.Amount=r.Amount AND e.Kind=r.Kind))
OR (r.Status<>'Approved' AND EXISTS(SELECT 1 FROM dbo.WalletEntries e WHERE e.Reference='money:'+CONVERT(varchar(20),r.Id)))
UNION ALL SELECT 'MoneyRequestMissingAdminAudit',COUNT(*) FROM dbo.MoneyRequests r WHERE r.Status<>'Pending' AND NOT EXISTS(
SELECT 1 FROM dbo.AdminAuditLogs a WHERE a.ActorId=r.ReviewerId AND a.TargetUserId=r.UserAccountId AND a.Action='ReviewMoneyRequest'
AND a.Reason LIKE 'Request #'+CONVERT(varchar(20),r.Id)+':%')
UNION ALL SELECT 'DisputeMissingAdminAudit',COUNT(*) FROM dbo.EscrowDisputes d JOIN dbo.Projects p ON p.Id=d.ProjectId WHERE d.Status='Resolved' AND NOT EXISTS(
SELECT 1 FROM dbo.AdminAuditLogs a WHERE a.ActorId=d.ReviewerId AND a.TargetUserId=p.ClientId AND a.Action='ResolveEscrowDispute'
AND a.Reason LIKE 'Dispute #'+CONVERT(varchar(20),d.Id)+':%')
UNION ALL SELECT 'PendingDisputeStateMismatch',COUNT(*) FROM dbo.EscrowDisputes d JOIN dbo.Projects p ON p.Id=d.ProjectId LEFT JOIN dbo.Contracts c ON c.Id=d.ContractId
WHERE d.Status='Pending' AND ((d.ContractId IS NULL AND p.Status<>'Disputed') OR (d.ContractId IS NOT NULL AND (c.Status<>'Disputed' OR c.ProjectId<>p.Id)))
UNION ALL SELECT 'ClientReviewWrongMembership',COUNT(*) FROM dbo.ClientReviews r JOIN dbo.Projects p ON p.Id=r.ProjectId
WHERE r.ClientId<>p.ClientId OR p.Status<>'Completed' OR NOT EXISTS(SELECT 1 FROM dbo.RoleAssignments a WHERE a.ProjectId=r.ProjectId AND a.FreelancerId=r.FreelancerId)
UNION ALL SELECT 'PortfolioProjectWrongMembership',COUNT(*) FROM dbo.PortfolioItems i WHERE i.ProjectId IS NOT NULL AND NOT EXISTS(
SELECT 1 FROM dbo.RoleAssignments a JOIN dbo.Projects p ON p.Id=a.ProjectId WHERE a.ProjectId=i.ProjectId AND a.FreelancerId=i.FreelancerId AND p.Status='Completed')
UNION ALL SELECT 'PayoutWrongRecipient',COUNT(*) FROM dbo.WalletEntries e JOIN dbo.EscrowMilestones m ON m.Id=e.EscrowMilestoneId
WHERE e.Kind='Payout' AND NOT EXISTS(SELECT 1 FROM dbo.EscrowShares s WHERE s.ProjectId=m.ProjectId AND s.UserAccountId=e.UserAccountId AND (s.ContractId=m.ContractId OR (s.ContractId IS NULL AND m.ContractId IS NULL)))
UNION ALL SELECT 'FeeJournalMismatch',COUNT(*) FROM dbo.EscrowMilestones m WHERE m.Status IN('Released','Refunded')
AND (m.Fee<>COALESCE((SELECT SUM(e.Amount) FROM dbo.WalletEntries e WHERE e.EscrowMilestoneId=m.Id AND e.Kind='PlatformFee'),0)
OR m.Refunded<>COALESCE((SELECT SUM(e.Amount) FROM dbo.WalletEntries e WHERE e.EscrowMilestoneId=m.Id AND e.Kind='Refund'),0))
UNION ALL SELECT 'ReviewReminderWrongMilestone',COUNT(*) FROM dbo.MilestoneReviewReminders r JOIN dbo.Deliverables d ON d.Id=r.DeliverableId
WHERE d.EscrowMilestoneId IS NULL OR d.EscrowMilestoneId<>r.EscrowMilestoneId
UNION ALL SELECT 'DuplicateReviewReminder',COUNT(*) FROM (SELECT DeliverableId FROM dbo.MilestoneReviewReminders GROUP BY DeliverableId HAVING COUNT(*)>1) duplicates;
SELECT * FROM @a ORDER BY Metric;
IF EXISTS(SELECT 1 FROM @a WHERE Violations<>0) THROW 51101,'Finance audit found invariant violations',1;
PRINT 'FINANCE_AUDIT_PASSED';
