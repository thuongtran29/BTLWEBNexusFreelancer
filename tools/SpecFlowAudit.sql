SET NOCOUNT ON;
IF DB_NAME()<>'NexusFreelanceMvp' THROW 51400,'Development database required',1;
DECLARE @a TABLE(Metric nvarchar(100),Violations bigint);
INSERT @a
SELECT 'NegotiationWrongParticipant',COUNT(*) FROM dbo.NegotiationMessages n JOIN dbo.Proposals p ON p.Id=n.ProposalId JOIN dbo.Projects j ON j.Id=p.ProjectId LEFT JOIN dbo.GroupBids g ON g.Id=p.GroupBidId
WHERE n.SenderId<>j.ClientId AND n.SenderId<>CASE WHEN p.GroupBidId IS NULL THEN p.ApplicantId ELSE g.LeaderId END
UNION ALL SELECT 'GroupNegotiationNotCanonical',COUNT(*) FROM dbo.NegotiationMessages n JOIN dbo.Proposals p ON p.Id=n.ProposalId WHERE p.GroupBidId IS NOT NULL AND p.Id<>(SELECT MIN(x.Id) FROM dbo.Proposals x WHERE x.GroupBidId=p.GroupBidId)
UNION ALL SELECT 'QuoteInvalidDeliveryDays',COUNT(*) FROM dbo.Proposals WHERE DeliveryDays NOT BETWEEN 1 AND 3650
UNION ALL SELECT 'GroupQuotePlanMismatch',COUNT(*) FROM dbo.Proposals p JOIN dbo.GroupBids g ON g.Id=p.GroupBidId WHERE p.DeliveryDays<>g.DeliveryDays OR COALESCE(p.MilestonePlan,'')<>COALESCE(g.MilestonePlan,'')
UNION ALL SELECT 'DuplicatePendingSkillRequest',COUNT(*) FROM(SELECT SkillClaimId FROM dbo.SkillEvidenceRequests WHERE Status='Pending' GROUP BY SkillClaimId HAVING COUNT(*)>1)d
UNION ALL SELECT 'RequestWithoutEvidence',COUNT(*) FROM dbo.SkillEvidenceRequests r WHERE NOT EXISTS(SELECT 1 FROM dbo.SkillEvidence e WHERE e.SkillEvidenceRequestId=r.Id)
UNION ALL SELECT 'ExamEvidenceWrongOwnerSkillOrScore',COUNT(*) FROM dbo.SkillEvidence e JOIN dbo.SkillEvidenceRequests r ON r.Id=e.SkillEvidenceRequestId JOIN dbo.SkillClaims c ON c.Id=r.SkillClaimId LEFT JOIN dbo.SkillExamAttempts a ON a.Id=e.SkillExamAttemptId
WHERE e.Type='Test' AND (a.Id IS NULL OR a.UserAccountId<>c.UserAccountId OR a.SkillTrackId<>c.SkillTrackId OR e.Score<>a.[Percent] OR e.Score IS NULL)
UNION ALL SELECT 'PortfolioEvidenceWrongOwner',COUNT(*) FROM dbo.SkillEvidence e JOIN dbo.SkillEvidenceRequests r ON r.Id=e.SkillEvidenceRequestId JOIN dbo.SkillClaims c ON c.Id=r.SkillClaimId JOIN dbo.PortfolioItems p ON p.Id=e.PortfolioItemId WHERE p.FreelancerId<>c.UserAccountId
UNION ALL SELECT 'ReviewedSkillRequestMissingAudit',COUNT(*) FROM dbo.SkillEvidenceRequests r JOIN dbo.SkillClaims c ON c.Id=r.SkillClaimId WHERE r.ReviewedAtUtc IS NOT NULL AND NOT EXISTS(
SELECT 1 FROM dbo.AdminAuditLogs a WHERE a.Action='ReviewSkillEvidence' AND a.ActorId=r.ReviewerId AND a.TargetUserId=c.UserAccountId AND a.Reason LIKE 'SkillRequest #'+CONVERT(varchar(20),r.Id)+':%')
UNION ALL SELECT 'SkillRequestReviewFieldsInvalid',COUNT(*) FROM dbo.SkillEvidenceRequests WHERE Status IN('Approved','Rejected','Expired') AND (ReviewerId IS NULL OR ReviewedAtUtc IS NULL OR LEN(LTRIM(RTRIM(Reason)))<5 OR Reason IS NULL)
UNION ALL SELECT 'SkillRequestExpiryMismatch',COUNT(*) FROM dbo.SkillEvidenceRequests r OUTER APPLY(SELECT MIN(e.ExpiresOn) d FROM dbo.SkillEvidence e WHERE e.SkillEvidenceRequestId=r.Id)x
WHERE r.ValidUntil<>x.d OR (r.ValidUntil IS NULL AND x.d IS NOT NULL) OR (r.ValidUntil IS NOT NULL AND x.d IS NULL)
UNION ALL SELECT 'DeletedSkillStillVerified',COUNT(*) FROM dbo.SkillEvidenceRequests r JOIN dbo.SkillClaims c ON c.Id=r.SkillClaimId WHERE c.IsDeleted=1 AND r.Status IN('Approved','Pending')
UNION ALL SELECT 'EvidenceInvalidHttpsUrl',COUNT(*) FROM dbo.SkillEvidence WHERE Type IN('Certificate','Portfolio') AND (Url IS NULL OR Url NOT LIKE 'https://%');
SELECT * FROM @a ORDER BY Metric;
IF EXISTS(SELECT 1 FROM @a WHERE Violations<>0) THROW 51401,'Specification flow invariant failed',1;
PRINT 'SPEC_FLOW_AUDIT_PASSED';
