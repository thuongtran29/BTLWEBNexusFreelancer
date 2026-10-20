using System.Data.Entity.Migrations;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Services;

namespace NexusFreelance.Web.Migrations
{
    internal sealed class Configuration : DbMigrationsConfiguration<AppDbContext>
    {
        public Configuration()
        {
            // Development MVP bootstrap; explicit, reviewed migrations are needed for production.
            AutomaticMigrationsEnabled = true;
            AutomaticMigrationDataLossAllowed = false;
            ContextKey = "NexusFreelance.Mvp";
        }

        protected override void Seed(AppDbContext context)
        {
            // SQLite is not supported; these CHECK constraints are applied to SQL Server after schema creation.
            context.Database.ExecuteSqlCommand("UPDATE p SET PublishedAtUtc=(SELECT MIN(a.CreatedAtUtc) FROM dbo.ActivityLogs a WHERE a.ProjectId=p.Id AND a.ActorId=p.ClientId AND a.Action=N'Published project') FROM dbo.Projects p WHERE p.PublishedAtUtc IS NULL AND EXISTS(SELECT 1 FROM dbo.ActivityLogs a WHERE a.ProjectId=p.Id AND a.ActorId=p.ClientId AND a.Action=N'Published project');");
            AddCheck(context,"CK_SharedKnowledge_Status","SharedKnowledgeEntries","[Status] IN ('Published','Withdrawn') AND [Version]>=1");
            AddCheck(context,"CK_GroupRecruitment_Status","GroupProjectRecruitments","[Status] IN ('Open','Closed')");
            AddCheck(context,"CK_GroupJoinRequest_Status","GroupJoinRequests","[Status] IN ('Pending','Invited','Rejected')");
            AddCheck(context, "CK_Project_Budget", "Projects", "[Budget] >= 1");
            AddCheck(context, "CK_Project_Type", "Projects", "[ProjectType] IN ('SOLO','TEAM','ENTERPRISE')");
            UpgradeProjectStatusCheck(context);
            AddCheck(context,"CK_EscrowMilestone_DueDay","EscrowMilestones","[DueDay] IS NULL OR [DueDay] BETWEEN 1 AND 3650");
            AddCheck(context, "CK_Requirement_Count", "RoleRequirements", "[RequiredCount] BETWEEN 1 AND 50");
            AddCheck(context, "CK_Proposal_Price", "Proposals", "[QuotedPrice] >= 0");
            UpgradeProposalStatusCheck(context);
            AddCheck(context, "CK_Task_Status", "Tasks", "[Status] IN ('ToDo','Doing','Review','Done')");
            AddCheck(context, "CK_Task_Priority", "Tasks", "[Priority] IS NULL OR [Priority] IN ('Low','Normal','High')");
            UpgradeTaskHistoryEventCheck(context);
            AddCheck(context, "CK_Deliverable_Status", "Deliverables", "[Status] IN ('Submitted','Approved','RevisionsRequested')");
            AddCheck(context, "CK_Review_Rating", "Reviews", "[Rating] BETWEEN 1 AND 5");
            AddCheck(context, "CK_AIJobDraft_Budget", "AIJobDrafts", "[Budget] >= 1");
            AddCheck(context, "CK_AIJobDraft_Type", "AIJobDrafts", "[ProjectType] IN ('SOLO','TEAM','ENTERPRISE')");
            AddCheck(context, "CK_AIJobRole_Count", "AIJobRoleSuggestions", "[RequiredCount] BETWEEN 1 AND 50");
            AddCheck(context, "CK_Matching_Points", "MatchingResults",
                "[CoreRolePoints] BETWEEN 0 AND 30 AND [BadgePoints] BETWEEN 0 AND 20 AND " +
                "[PortfolioPoints] BETWEEN 0 AND 15 AND [AvailabilityPoints] BETWEEN 0 AND 15 AND " +
                "[ReliabilityPoints] BETWEEN 0 AND 10 AND [BudgetPoints] BETWEEN 0 AND 10 AND " +
                "[TotalPoints] = [CoreRolePoints]+[BadgePoints]+[PortfolioPoints]+[AvailabilityPoints]+[ReliabilityPoints]+[BudgetPoints]");
            // New sittings use >80% of actual correct answers. Preserve historical
            // awards, and allow a rounded 80% failed attempt at the exact boundary.
            context.Database.ExecuteSqlCommand("IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_ExamAttempt_Grade' AND CHARINDEX('[Percent]<(80)',definition)>0) ALTER TABLE dbo.SkillExamAttempts DROP CONSTRAINT CK_ExamAttempt_Grade;");
            AddCheck(context, "CK_ExamAttempt_Grade", "SkillExamAttempts",
                "[QuestionCount] > 0 AND [CorrectCount] BETWEEN 0 AND [QuestionCount] AND " +
                "[Percent] BETWEEN 0 AND 100 AND (([Passed] = 1 AND [Percent] >= 80) OR ([Passed] = 0 AND [Percent] <= 80))");
            AddCheck(context, "CK_ExamAttempt_Timing", "SkillExamAttempts",
                "([StartedAtUtc] IS NULL AND [DurationSeconds] IS NULL AND [BrowserFocusLossCount] IS NULL) OR " +
                "([StartedAtUtc] IS NOT NULL AND [DurationSeconds] BETWEEN 0 AND 1200 AND " +
                "[BrowserFocusLossCount] BETWEEN 0 AND 999 AND [StartedAtUtc]<=[SubmittedAtUtc])");
            AddCheck(context, "CK_ExamSession_Times", "SkillExamSessions",
                "[ExpiresAtUtc]>[StartedAtUtc] AND " +
                "([CompletedAtUtc] IS NULL OR ([CompletedAtUtc]>=[StartedAtUtc] AND [CompletedAtUtc]<=[ExpiresAtUtc]))");
            AddCheck(context, "CK_ExamAnswer_Choice", "SkillExamAnswers", "[SelectedChoice] IN ('A','B','C','D')");
            AddCheck(context, "CK_ExamQuestion_Correct", "SkillExamQuestions", "[CorrectChoice] IN ('A','B','C','D')");
            AddCheck(context, "CK_EarnedSkillBadge_Revocation", "EarnedSkillBadges",
                "([RevokedAtUtc] IS NULL AND [RevokedById] IS NULL AND [RevocationReason] IS NULL) OR " +
                "([RevokedAtUtc] IS NOT NULL AND [RevokedById] IS NOT NULL AND [RevocationReason] IS NOT NULL " +
                "AND LEN(LTRIM(RTRIM([RevocationReason]))) BETWEEN 5 AND 200 AND [RevokedAtUtc]>=[IssuedAtUtc])");
            AddCheck(context, "CK_ProjectMilestone_Status", "ProjectMilestones", "[Status] IN ('Planned','Completed')");
            AddCheck(context, "CK_ProjectMilestone_Completion", "ProjectMilestones",
                "([Status]='Planned' AND [CompletedAtUtc] IS NULL) OR " +
                "([Status]='Completed' AND [CompletedAtUtc] IS NOT NULL AND [CompletedAtUtc]>=[CreatedAtUtc])");
            AddCheck(context, "CK_FreelancerGroupMember_Status", "FreelancerGroupMembers",
                "[Status] IN ('Invited','Active','Declined','Removed','Left')");
            context.Database.ExecuteSqlCommand(@"IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_GroupBid_Status' AND definition NOT LIKE '%''Shortlisted''%') ALTER TABLE dbo.GroupBids DROP CONSTRAINT CK_GroupBid_Status");
            AddCheck(context, "CK_GroupBid_Status", "GroupBids",
                "[Status] IN ('Pending','Shortlisted','Accepted','Rejected','Withdrawn')");
            AddCheck(context, "CK_ExternalCertification_Status", "ExternalCertifications",
                "[Status] IN ('Pending','Verified','Rejected')");
            AddCheck(context, "CK_ExternalCertification_Dates", "ExternalCertifications",
                "[ExpiresOn] IS NULL OR [ExpiresOn] > [IssuedOn]");
            AddCheck(context, "CK_ProjectAbuseReport_Status", "ProjectAbuseReports",
                "[Status] IN ('Pending','Confirmed','Dismissed')");
            AddCheck(context, "CK_ProjectAbuseReport_Category", "ProjectAbuseReports",
                "[Category] IN ('Fraud','Spam','Abuse','Other')");
            AddCheck(context, "CK_ProjectAbuseReport_Review", "ProjectAbuseReports",
                "([Status]='Pending' AND [ReviewerId] IS NULL AND [ReviewedAtUtc] IS NULL AND [Resolution] IS NULL) OR " +
                "([Status]<>'Pending' AND [ReviewerId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL AND [Resolution] IS NOT NULL)");
            AddCheck(context, "CK_PasswordResetToken_Times", "PasswordResetTokens",
                "[ExpiresAtUtc]>[CreatedAtUtc] AND ([ConsumedAtUtc] IS NULL OR [ConsumedAtUtc]>=[CreatedAtUtc])");
            AddCheck(context, "CK_EmailVerificationToken_Times", "EmailVerificationTokens",
                "[ExpiresAtUtc]>[CreatedAtUtc] AND ([ConsumedAtUtc] IS NULL OR [ConsumedAtUtc]>=[CreatedAtUtc])");
            AddCheck(context, "CK_WorkspaceFileVersion_Fields", "WorkspaceFileVersions",
                "[VersionNumber]>=1 AND [ByteLength] BETWEEN 1 AND 8388608 AND DATALENGTH([Data])=[ByteLength]");
            // A reviewer may close an old report and a user may submit a new one,
            // but two pending reports for the same project/user are never valid.
            // The database index backs up the serializable application transaction.
            context.Database.ExecuteSqlCommand(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id=OBJECT_ID(N'dbo.ProjectAbuseReports')
               AND name=N'UX_ProjectAbuseReports_OnePending')
    CREATE UNIQUE NONCLUSTERED INDEX UX_ProjectAbuseReports_OnePending
    ON dbo.ProjectAbuseReports(ProjectId, ReporterId)
    WHERE [Status]='Pending'");
            AddCheck(context, "CK_Wallet_NonNegative", "WalletAccounts", "Balance>=0 AND Held>=0 AND Currency='VND'");
            AddCheck(context, "CK_WalletEntry_Amount", "WalletEntries", "Amount>0 AND Currency='VND' AND Amount=FLOOR(Amount)");
            AddCheck(context, "CK_MoneyRequest_State", "MoneyRequests", "Kind IN ('Deposit','Withdrawal') AND Status IN ('Pending','Approved','Rejected') AND Amount>0 AND Amount=FLOOR(Amount)");
            AddCheck(context, "CK_EscrowMilestone_State", "EscrowMilestones", "Amount>0 AND Amount=FLOOR(Amount) AND Fee>=0 AND Refunded>=0 AND Fee+Refunded<=Amount AND Status IN ('Planned','Funded','Submitted','RevisionsRequested','Released','Refunded')");
            AddCheck(context, "CK_EscrowShare_Weight", "EscrowShares", "Weight>0");
            AddCheck(context, "CK_RegistrationCode_Attempts", "RegistrationCodes", "FailedAttempts BETWEEN 0 AND 5 AND ExpiresAtUtc>CreatedAtUtc");
            context.Database.ExecuteSqlCommand(@"IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Users') AND name='UX_Users_GoogleRole')
CREATE UNIQUE INDEX UX_Users_GoogleRole ON dbo.Users(GoogleSubject,Role) WHERE GoogleSubject IS NOT NULL");
            AddCheck(context, "CK_ClientReview_Rating", "ClientReviews", "Rating BETWEEN 1 AND 5");
            AddCheck(context, "CK_ClientProfile_Type", "ClientProfiles", "ClientType IN ('Individual','Business') AND (ClientType<>'Business' OR LEN(LTRIM(RTRIM(CompanyName)))>0)");
            AddCheck(context, "CK_Project_FeeRate", "Projects", "PlatformFeeRate BETWEEN 0 AND 1");
            AddCheck(context, "CK_WalletEntry_Ledger", "WalletEntries", "(Kind='Deposit' AND BalanceDelta=Amount AND HeldDelta=0) OR (Kind='Withdrawal' AND BalanceDelta=-Amount AND HeldDelta=0) OR (Kind='EscrowHold' AND BalanceDelta=-Amount AND HeldDelta=Amount) OR (Kind='EscrowSettle' AND BalanceDelta=0 AND HeldDelta=-Amount) OR (Kind IN ('Refund','Payout') AND BalanceDelta=Amount AND HeldDelta=0) OR (Kind='PlatformFee' AND BalanceDelta=0 AND HeldDelta=0)");
            AddCheck(context, "CK_MoneyRequest_Review", "MoneyRequests", "(Status='Pending' AND ReviewerId IS NULL AND ReviewedAtUtc IS NULL AND Resolution IS NULL) OR (Status<>'Pending' AND ReviewerId IS NOT NULL AND ReviewedAtUtc IS NOT NULL AND Resolution IS NOT NULL)");
            AddCheck(context,"CK_SkillClaim_Level","SkillClaims","Level BETWEEN 1 AND 5 AND YearsExperience BETWEEN 0 AND 60");
            AddCheck(context,"CK_SkillEvidenceRequest_Status","SkillEvidenceRequests","Status IN ('Pending','Approved','Rejected','Expired','Cancelled')");
            AddCheck(context,"CK_SkillEvidence_Source","SkillEvidence","(Type='Test' AND SkillExamAttemptId IS NOT NULL AND Score BETWEEN 0 AND 100) OR (Type='Certificate' AND Url IS NOT NULL AND Issuer IS NOT NULL AND IssuedOn IS NOT NULL AND (ExpiresOn IS NULL OR ExpiresOn>IssuedOn)) OR (Type='Portfolio' AND Url IS NOT NULL)");
            context.Database.ExecuteSqlCommand(@"IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.SkillEvidenceRequests') AND name='UX_SkillEvidenceRequest_Pending') CREATE UNIQUE INDEX UX_SkillEvidenceRequest_Pending ON dbo.SkillEvidenceRequests(SkillClaimId) WHERE Status='Pending'");
            AddCheck(context,"CK_ProjectSkill_Level","ProjectSkillRequirements","MinimumLevel BETWEEN 1 AND 5");
            AddCheck(context,"CK_WalletTopup_Amount","WalletTopups","Amount BETWEEN 10000 AND 9999999999 AND Amount=FLOOR(Amount)");
            AddCheck(context,"CK_WalletTopup_State","WalletTopups","Status IN ('Pending','Paid','Failed') AND ExpiresAtUtc>CreatedAtUtc AND ((Status='Paid' AND CapturedAtUtc IS NOT NULL AND ProviderTransactionNo IS NOT NULL) OR (Status<>'Paid' AND CapturedAtUtc IS NULL AND ProviderTransactionNo IS NULL))");
            AddCheck(context,"CK_Certification_Level","ExternalCertifications","ProficiencyLevel IS NULL OR ProficiencyLevel BETWEEN 1 AND 5");
            context.Database.ExecuteSqlCommand("IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.WalletTopups') AND name='UX_WalletTopup_Provider') CREATE UNIQUE INDEX UX_WalletTopup_Provider ON dbo.WalletTopups(Environment,MerchantCode,ProviderTransactionNo) WHERE ProviderTransactionNo IS NOT NULL;");
            SkillExamSeeder.Seed(context);
            SkillCatalogSeed.Apply(context);
            ContractSchemaSeed.Apply(context);
        }

        private static void AddCheck(AppDbContext context, string name, string table, string expression)
        {
            // All identifiers and expressions are constants passed by application code, never user input.
            context.Database.ExecuteSqlCommand(
                "IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = '" + name + "') " +
                "ALTER TABLE [dbo].[" + table + "] ADD CONSTRAINT [" + name + "] CHECK (" + expression + ")");
        }

        private static void UpgradeProjectStatusCheck(AppDbContext context)
        {
            const string check = "[Status] IN ('Draft','Published','Paused','Cancelled','InProgress','AwaitingEscrow','Disputed','Completed')";
            // Existing MVP installations have a narrower CHECK. Upgrade it without touching data.
            context.Database.ExecuteSqlCommand(@"
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Project_Status'
    AND parent_object_id = OBJECT_ID('dbo.Projects')
    AND (definition NOT LIKE '%''Paused''%' OR definition NOT LIKE '%''Cancelled''%' OR definition NOT LIKE '%''AwaitingEscrow''%' OR definition NOT LIKE '%''Disputed''%'))
    ALTER TABLE [dbo].[Projects] DROP CONSTRAINT [CK_Project_Status]");
            AddCheck(context, "CK_Project_Status", "Projects", check);
        }

        private static void UpgradeProposalStatusCheck(AppDbContext context)
        {
            const string check = "[Status] IN ('Pending','Shortlisted','Accepted','Withdrawn','Rejected')";
            context.Database.ExecuteSqlCommand(@"
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Proposal_Status'
    AND parent_object_id = OBJECT_ID('dbo.Proposals')
    AND (definition NOT LIKE '%''Withdrawn''%' OR definition NOT LIKE '%''Rejected''%' OR definition NOT LIKE '%''Shortlisted''%'))
    ALTER TABLE [dbo].[Proposals] DROP CONSTRAINT [CK_Proposal_Status]");
            AddCheck(context, "CK_Proposal_Status", "Proposals", check);
        }

        private static void UpgradeTaskHistoryEventCheck(AppDbContext context)
        {
            // Development migration may add edit snapshot columns to an existing database.
            // Replace only the old CHECK definition, retaining every history row.
            context.Database.ExecuteSqlCommand(@"
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='CK_TaskHistory_Event'
    AND parent_object_id=OBJECT_ID('dbo.TaskHistories')
    AND definition NOT LIKE '%''Edited''%')
    ALTER TABLE [dbo].[TaskHistories] DROP CONSTRAINT [CK_TaskHistory_Event]");
            AddCheck(context, "CK_TaskHistory_Event", "TaskHistories",
                "([EventType]='Created' AND [OldStatus] IS NULL AND [NewStatus]='ToDo') OR " +
                "([EventType]='StatusChanged' AND [OldStatus] IN ('ToDo','Doing','Review','Done') " +
                "AND [NewStatus] IN ('ToDo','Doing','Review','Done') AND [OldStatus]<>[NewStatus]) OR " +
                "([EventType]='Edited' AND [OldStatus]=[NewStatus] AND " +
                "[OldStatus] IN ('ToDo','Doing','Review','Done') AND [OldTitle] IS NOT NULL " +
                "AND [NewTitle] IS NOT NULL AND [OldAssigneeId] IS NOT NULL AND [NewAssigneeId] IS NOT NULL)");
        }
    }
}
