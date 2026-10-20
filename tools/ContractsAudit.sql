-- Read-only contract, acceptance, workspace and PSP accounting invariants.
SET NOCOUNT ON;
IF DB_NAME()<>'NexusFreelanceMvp' THROW 51610,'Development database required',1;
DECLARE @a TABLE(Metric nvarchar(100),Violations bigint);
INSERT @a
SELECT 'ContractMissingCurrentVersion',COUNT(*) FROM dbo.Contracts c WHERE NOT EXISTS(SELECT 1 FROM dbo.ContractVersions v WHERE v.ContractId=c.Id AND v.VersionNumber=c.CurrentVersion)
UNION ALL SELECT 'ContractInvalidSnapshot',COUNT(*) FROM dbo.ContractVersions v WHERE ISJSON(v.SnapshotJson)<>1 OR LEN(v.Hash)<>64
 OR TRY_CONVERT(decimal(18,0),JSON_VALUE(v.SnapshotJson,'$.Price'))<>v.Price
 OR TRY_CONVERT(decimal(6,4),JSON_VALUE(v.SnapshotJson,'$.FeeRate'))<>v.FeeRate
 OR TRY_CONVERT(int,JSON_VALUE(v.SnapshotJson,'$.DeliveryDays'))<>v.DeliveryDays
 OR JSON_VALUE(v.SnapshotJson,'$.Currency')<>'VND'
UNION ALL SELECT 'ContractPlanTotalMismatch',COUNT(*) FROM dbo.Contracts c JOIN dbo.ContractVersions v ON v.ContractId=c.Id AND v.VersionNumber=c.CurrentVersion
 WHERE v.Price<>COALESCE((SELECT SUM(m.Amount) FROM dbo.EscrowMilestones m WHERE m.ContractId=c.Id),0)
UNION ALL SELECT 'ContractRosterWeightMismatch',COUNT(*) FROM dbo.Contracts c JOIN dbo.ContractVersions v ON v.ContractId=c.Id AND v.VersionNumber=c.CurrentVersion
 WHERE v.Price<>COALESCE((SELECT SUM(m.Weight) FROM dbo.ContractMembers m WHERE m.ContractId=c.Id),0)
 OR v.Price<>COALESCE((SELECT SUM(s.Weight) FROM dbo.EscrowShares s WHERE s.ContractId=c.Id),0)
UNION ALL SELECT 'ContractShareScopeMismatch',COUNT(*) FROM dbo.EscrowShares s JOIN dbo.Contracts c ON c.Id=s.ContractId
 WHERE s.ProjectId<>c.ProjectId OR NOT EXISTS(SELECT 1 FROM dbo.ContractMembers m WHERE m.ContractId=c.Id AND m.UserAccountId=s.UserAccountId AND m.Weight=s.Weight)
UNION ALL SELECT 'ContractMemberProposalMismatch',COUNT(*) FROM dbo.ContractMembers m JOIN dbo.Contracts c ON c.Id=m.ContractId JOIN dbo.Proposals p ON p.Id=m.ProposalId
 WHERE p.ProjectId<>c.ProjectId OR p.ApplicantId<>m.UserAccountId OR p.QuotedPrice<>m.Weight OR (c.Status<>'Declined' AND p.Status<>'Accepted')
UNION ALL SELECT 'ContractCounterpartyNotInRoster',COUNT(*) FROM dbo.Contracts c WHERE NOT EXISTS(SELECT 1 FROM dbo.ContractMembers m WHERE m.ContractId=c.Id AND m.UserAccountId=c.CounterpartyId)
UNION ALL SELECT 'ContractAcceptanceWrongHashOrSigner',COUNT(*) FROM dbo.ContractAcceptances a JOIN dbo.ContractVersions v ON v.Id=a.ContractVersionId JOIN dbo.Contracts c ON c.Id=v.ContractId JOIN dbo.Projects p ON p.Id=c.ProjectId
 WHERE a.AcceptedHash<>v.Hash OR (a.UserAccountId<>p.ClientId AND NOT EXISTS(SELECT 1 FROM dbo.ContractMembers m WHERE m.ContractId=c.Id AND m.UserAccountId=a.UserAccountId))
UNION ALL SELECT 'ContractReadyWithoutAllSignatures',COUNT(*) FROM dbo.Contracts c JOIN dbo.Projects p ON p.Id=c.ProjectId JOIN dbo.ContractVersions v ON v.ContractId=c.Id AND v.VersionNumber=c.CurrentVersion
 WHERE c.Status IN('AwaitingEscrow','Active','Disputed','Completed','Terminated') AND
 (NOT EXISTS(SELECT 1 FROM dbo.ContractAcceptances a WHERE a.ContractVersionId=v.Id AND a.UserAccountId=p.ClientId AND a.AcceptedHash=v.Hash)
 OR EXISTS(SELECT 1 FROM dbo.ContractMembers m WHERE m.ContractId=c.Id AND NOT EXISTS(SELECT 1 FROM dbo.ContractAcceptances a WHERE a.ContractVersionId=v.Id AND a.UserAccountId=m.UserAccountId AND a.AcceptedHash=v.Hash)))
UNION ALL SELECT 'ContractWorkspaceScopeMismatch',COUNT(*) FROM dbo.Workspaces w JOIN dbo.Contracts c ON c.Id=w.ContractId WHERE w.ProjectId<>c.ProjectId
UNION ALL SELECT 'ContractMissingWorkspace',COUNT(*) FROM dbo.Contracts c WHERE (SELECT COUNT(*) FROM dbo.Workspaces w WHERE w.ContractId=c.Id)<>1
UNION ALL SELECT 'ContractMilestoneScopeMismatch',COUNT(*) FROM dbo.EscrowMilestones m JOIN dbo.Contracts c ON c.Id=m.ContractId WHERE m.ProjectId<>c.ProjectId
UNION ALL SELECT 'ContractDeliveryScopeMismatch',COUNT(*) FROM dbo.Deliverables d JOIN dbo.Workspaces w ON w.Id=d.WorkspaceId JOIN dbo.EscrowMilestones m ON m.Id=d.EscrowMilestoneId
 WHERE m.ProjectId<>w.ProjectId OR COALESCE(m.ContractId,-1)<>COALESCE(w.ContractId,-1)
UNION ALL SELECT 'ContractTaskAssigneeOutsideRoster',COUNT(*) FROM dbo.Tasks t JOIN dbo.Workspaces w ON w.Id=t.WorkspaceId
 WHERE w.ContractId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.ContractMembers m WHERE m.ContractId=w.ContractId AND m.UserAccountId=t.AssigneeId)
UNION ALL SELECT 'ContractDisputeScopeMismatch',COUNT(*) FROM dbo.EscrowDisputes d JOIN dbo.Contracts c ON c.Id=d.ContractId
 WHERE d.ProjectId<>c.ProjectId OR (d.Status='Pending' AND c.Status<>'Disputed')
UNION ALL SELECT 'ContractCompletedWithUnsettledMoney',COUNT(*) FROM dbo.Contracts c WHERE c.Status IN('Completed','Terminated') AND EXISTS(SELECT 1 FROM dbo.EscrowMilestones m WHERE m.ContractId=c.Id AND m.Status NOT IN('Released','Refunded'))
UNION ALL SELECT 'ContractActiveWithoutFirstAssurance',COUNT(*) FROM dbo.Contracts c WHERE c.Status='Active' AND (SELECT TOP(1) m.Status FROM dbo.EscrowMilestones m WHERE m.ContractId=c.Id ORDER BY m.Id)='Planned'
UNION ALL SELECT 'PspIntentWrongObligation',COUNT(*) FROM dbo.PaymentIntents p JOIN dbo.EscrowMilestones m ON m.Id=p.EscrowMilestoneId WHERE p.ContractId<>m.ContractId OR p.Amount<>m.Amount OR p.Currency<>'VND'
UNION ALL SELECT 'PspPaidWithoutCapture',COUNT(*) FROM dbo.PaymentIntents p JOIN dbo.Contracts c ON c.Id=p.ContractId JOIN dbo.Projects j ON j.Id=c.ProjectId
 WHERE p.Status IN('Paid','PaidUnallocated') AND (p.PaidAtUtc IS NULL OR p.ProviderTransactionNo IS NULL OR
 (SELECT COUNT(*) FROM dbo.WalletEntries e WHERE e.Reference='vnpay:capture:'+CONVERT(varchar(20),p.Id) AND e.UserAccountId=j.ClientId AND e.Amount=p.Amount AND e.EscrowMilestoneId=p.EscrowMilestoneId AND e.Kind='PspCapture')<>1)
UNION ALL SELECT 'PspUnpaidHasCapture',COUNT(*) FROM dbo.PaymentIntents p WHERE p.Status NOT IN('Paid','PaidUnallocated') AND EXISTS(SELECT 1 FROM dbo.WalletEntries e WHERE e.Reference='vnpay:capture:'+CONVERT(varchar(20),p.Id))
UNION ALL SELECT 'PspAllocatedWithoutHold',COUNT(*) FROM dbo.PaymentIntents p WHERE p.Status='Paid' AND NOT EXISTS(SELECT 1 FROM dbo.WalletEntries e WHERE e.Kind='EscrowHold' AND e.EscrowMilestoneId=p.EscrowMilestoneId AND e.Amount=p.Amount)
UNION ALL SELECT 'PspRefundWrongAmountOrPayment',COUNT(*) FROM dbo.PspRefunds r JOIN dbo.PaymentIntents p ON p.Id=r.PaymentIntentId WHERE r.Amount>p.Amount OR p.Status NOT IN('Paid','PaidUnallocated')
UNION ALL SELECT 'PspRefundMissingReservation',COUNT(*) FROM dbo.PspRefunds r JOIN dbo.PaymentIntents p ON p.Id=r.PaymentIntentId JOIN dbo.Contracts c ON c.Id=p.ContractId JOIN dbo.Projects j ON j.Id=c.ProjectId
 WHERE (SELECT COUNT(*) FROM dbo.WalletEntries e WHERE e.Reference='vnpay:refund:'+r.RequestId AND e.Kind='PspRefundReserve' AND e.Amount=r.Amount AND e.UserAccountId=j.ClientId)<>1
UNION ALL SELECT 'PspRefundRestoreMismatch',COUNT(*) FROM dbo.PspRefunds r WHERE
 (r.Status='Failed' AND (SELECT COUNT(*) FROM dbo.WalletEntries e WHERE e.Reference='vnpay:refund-restore:'+CONVERT(varchar(20),r.Id) AND e.Kind='PspRefundRestore' AND e.Amount=r.Amount)<>1)
 OR (r.Status<>'Failed' AND EXISTS(SELECT 1 FROM dbo.WalletEntries e WHERE e.Reference='vnpay:refund-restore:'+CONVERT(varchar(20),r.Id)))
UNION ALL SELECT 'PspRefundTerminalWithoutProof',COUNT(*) FROM dbo.PspRefunds r WHERE r.Status IN('Succeeded','Failed') AND (r.ResponseDigest IS NULL OR LEN(r.ResponseDigest)<>64)
UNION ALL SELECT 'PspRefundDispatchTimestampMismatch',COUNT(*) FROM dbo.PspRefunds r WHERE (r.Status='Queued' AND r.AttemptedAtUtc IS NOT NULL) OR (r.Status<>'Queued' AND r.AttemptedAtUtc IS NULL)
UNION ALL SELECT 'ImmutableContractTriggersMissingOrDisabled',3-COUNT(*) FROM sys.triggers WHERE name IN('TR_ContractVersions_Immutable','TR_ContractMembers_Immutable','TR_ContractAcceptances_Immutable') AND is_disabled=0;
;WITH live AS (
 SELECT m.*,ROW_NUMBER() OVER(PARTITION BY ContractId ORDER BY Id)-1 AS PlanIndex FROM dbo.EscrowMilestones m WHERE ContractId IS NOT NULL
)
INSERT @a
SELECT 'ContractSignedMilestoneMetadataMismatch',COUNT(*)
FROM dbo.Contracts c JOIN dbo.ContractVersions v ON v.ContractId=c.Id AND v.VersionNumber=c.CurrentVersion
CROSS APPLY OPENJSON(v.SnapshotJson,'$.Milestones') j
LEFT JOIN live m ON m.ContractId=c.Id AND m.PlanIndex=TRY_CONVERT(int,j.[key])
WHERE m.Id IS NULL OR EXISTS (
 SELECT CONVERT(varbinary(max),m.Title),m.Amount,m.DueDay,CONVERT(varbinary(max),m.AcceptanceCriteria)
 EXCEPT SELECT CONVERT(varbinary(max),JSON_VALUE(j.value,'$.Title')),TRY_CONVERT(decimal(18,0),JSON_VALUE(j.value,'$.Amount')),
 TRY_CONVERT(int,JSON_VALUE(j.value,'$.DueDay')),CONVERT(varbinary(max),JSON_VALUE(j.value,'$.AcceptanceCriteria'))
);
INSERT @a
SELECT 'ContractSignedMilestoneCountMismatch',COUNT(*) FROM dbo.Contracts c
JOIN dbo.ContractVersions v ON v.ContractId=c.Id AND v.VersionNumber=c.CurrentVersion
WHERE (SELECT COUNT(*) FROM OPENJSON(v.SnapshotJson,'$.Milestones'))<>(SELECT COUNT(*) FROM dbo.EscrowMilestones m WHERE m.ContractId=c.Id);
SELECT * FROM @a ORDER BY Metric;
IF EXISTS(SELECT 1 FROM @a WHERE Violations<>0) THROW 51611,'Contract or PSP invariant failed',1;
PRINT 'CONTRACTS_AUDIT_PASSED';
