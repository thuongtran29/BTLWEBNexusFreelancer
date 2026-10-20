using System.Data.Entity;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext() : base("name=NexusDb") { }
        public DbSet<SharedKnowledgeEntry> SharedKnowledgeEntries {get;set;}
        public DbSet<GroupProjectRecruitment> GroupProjectRecruitments {get;set;}
        public DbSet<GroupJoinRequest> GroupJoinRequests {get;set;}
        public DbSet<ProjectSkillRequirement> ProjectSkillRequirements { get; set; }
        public DbSet<DirectConversation> DirectConversations { get; set; }
        public DbSet<DirectMessage> DirectMessages { get; set; }
        public DbSet<WalletTopup> WalletTopups { get; set; }
        public DbSet<FreelancerSecondaryRole> FreelancerSecondaryRoles { get; set; }
        public DbSet<UserAccessLog> UserAccessLogs { get; set; }

        public DbSet<UserAccount> Users { get; set; }
        public DbSet<ProjectContract> Contracts { get; set; }
        public DbSet<ContractVersion> ContractVersions { get; set; }
        public DbSet<ContractMember> ContractMembers { get; set; }
        public DbSet<ContractAcceptance> ContractAcceptances { get; set; }
        public DbSet<PaymentIntent> PaymentIntents { get; set; }
        public DbSet<PspReceipt> PspReceipts { get; set; }
        public DbSet<PspRefund> PspRefunds { get; set; }
        public DbSet<SkillClaim> SkillClaims { get; set; }
        public DbSet<SkillEvidenceRequest> SkillEvidenceRequests { get; set; }
        public DbSet<SkillEvidence> SkillEvidence { get; set; }
        public DbSet<NegotiationMessage> NegotiationMessages { get; set; }
        public DbSet<ClientProfile> ClientProfiles { get; set; }
        public DbSet<ClientReview> ClientReviews { get; set; }
        public DbSet<RegistrationCode> RegistrationCodes { get; set; }
        public DbSet<WalletAccount> WalletAccounts { get; set; }
        public DbSet<WalletEntry> WalletEntries { get; set; }
        public DbSet<MoneyRequest> MoneyRequests { get; set; }
        public DbSet<EscrowMilestone> EscrowMilestones { get; set; }
        public DbSet<MilestoneReviewReminder> MilestoneReviewReminders { get; set; }
        public DbSet<EscrowShare> EscrowShares { get; set; }
        public DbSet<EscrowDispute> EscrowDisputes { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
        public DbSet<EmailVerificationToken> EmailVerificationTokens { get; set; }
        public DbSet<AdminAuditLog> AdminAuditLogs { get; set; }
        public DbSet<ProjectAbuseReport> ProjectAbuseReports { get; set; }
        public DbSet<FreelancerGroup> FreelancerGroups { get; set; }
        public DbSet<FreelancerGroupMember> FreelancerGroupMembers { get; set; }
        public DbSet<FreelancerGroupEvent> FreelancerGroupEvents { get; set; }
        public DbSet<GroupBid> GroupBids { get; set; }
        public DbSet<UserNotification> Notifications { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<RoleRequirement> RoleRequirements { get; set; }
        public DbSet<Proposal> Proposals { get; set; }
        public DbSet<RoleAssignment> RoleAssignments { get; set; }
        public DbSet<ProjectTeam> ProjectTeams { get; set; }
        public DbSet<TeamMember> TeamMembers { get; set; }
        public DbSet<Workspace> Workspaces { get; set; }
        public DbSet<WorkTask> Tasks { get; set; }
        public DbSet<TaskHistory> TaskHistories { get; set; }
        public DbSet<WorkspaceMessage> WorkspaceMessages { get; set; }
        public DbSet<ProjectMilestone> ProjectMilestones { get; set; }
        public DbSet<WorkspaceMeeting> WorkspaceMeetings { get; set; }
        public DbSet<ProjectMemory> ProjectMemories { get; set; }
        public DbSet<ContributionRecord> ContributionRecords { get; set; }
        public DbSet<XPTransaction> XPTransactions { get; set; }
        public DbSet<SkillDNAHistory> SkillDNAHistories { get; set; }
        public DbSet<Deliverable> Deliverables { get; set; }
        public DbSet<WorkspaceFile> WorkspaceFiles { get; set; }
        public DbSet<WorkspaceFileVersion> WorkspaceFileVersions { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<ActivityLog> ActivityLogs { get; set; }
        public DbSet<AIJobDraft> AIJobDrafts { get; set; }
        public DbSet<AIJobDraftVersion> AIJobDraftVersions { get; set; }
        public DbSet<AIJobRoleSuggestion> AIJobRoleSuggestions { get; set; }
        public DbSet<FreelancerProfile> FreelancerProfiles { get; set; }
        public DbSet<PortfolioItem> PortfolioItems { get; set; }
        public DbSet<MatchingResult> MatchingResults { get; set; }
        public DbSet<SkillTrack> SkillTracks { get; set; }
        public DbSet<SkillExamQuestion> SkillExamQuestions { get; set; }
        public DbSet<SkillExamAttempt> SkillExamAttempts { get; set; }
        public DbSet<SkillExamSession> SkillExamSessions { get; set; }
        public DbSet<SkillExamAnswer> SkillExamAnswers { get; set; }
        public DbSet<EarnedSkillBadge> EarnedSkillBadges { get; set; }
        public DbSet<ExternalCertification> ExternalCertifications { get; set; }

        protected override void OnModelCreating(DbModelBuilder m)
        {
            base.OnModelCreating(m);
            PlatformModelConfig.Configure(m);
            KnowledgeRecruitmentModelConfig.Configure(m);
            ContractModelConfig.Configure(m);
            PspModelConfig.Configure(m);
            m.Entity<FreelancerSecondaryRole>().ToTable("FreelancerSecondaryRoles");
            m.Entity<FreelancerSecondaryRole>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<FreelancerSecondaryRole>().HasRequired(x => x.SkillTrack).WithMany().HasForeignKey(x => x.SkillTrackId).WillCascadeOnDelete(false);
            m.Entity<FreelancerSecondaryRole>().HasIndex(x => new { x.UserAccountId, x.SkillTrackId }).IsUnique();
            m.Entity<UserAccessLog>().ToTable("UserAccessLogs");
            m.Entity<UserAccessLog>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<UserAccessLog>().HasIndex(x => new { x.UserAccountId, x.AccessedAtUtc });
            m.Entity<UserAccessLog>().HasIndex(x => x.AccessedAtUtc);
            m.Entity<UserAccessLog>().Property(x => x.AccessedAtUtc).HasColumnType("datetime2");
            m.Entity<MilestoneReviewReminder>().ToTable("MilestoneReviewReminders");
            m.Entity<MilestoneReviewReminder>().HasRequired(x => x.Milestone).WithMany().HasForeignKey(x => x.EscrowMilestoneId).WillCascadeOnDelete(false);
            m.Entity<MilestoneReviewReminder>().HasRequired(x => x.Delivery).WithMany().HasForeignKey(x => x.DeliverableId).WillCascadeOnDelete(false);
            m.Entity<MilestoneReviewReminder>().HasIndex(x => x.DeliverableId).IsUnique();
            m.Entity<SkillClaim>().ToTable("SkillClaims");
            m.Entity<SkillClaim>().HasRequired(x=>x.User).WithMany().HasForeignKey(x=>x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<SkillClaim>().HasRequired(x=>x.Skill).WithMany().HasForeignKey(x=>x.SkillTrackId).WillCascadeOnDelete(false);
            m.Entity<SkillClaim>().HasIndex(x=>new{x.UserAccountId,x.SkillTrackId}).IsUnique();
            m.Entity<SkillEvidenceRequest>().ToTable("SkillEvidenceRequests");
            m.Entity<SkillEvidenceRequest>().HasRequired(x=>x.Claim).WithMany().HasForeignKey(x=>x.SkillClaimId).WillCascadeOnDelete(false);
            m.Entity<SkillEvidenceRequest>().HasOptional(x=>x.Reviewer).WithMany().HasForeignKey(x=>x.ReviewerId).WillCascadeOnDelete(false);
            m.Entity<SkillEvidence>().ToTable("SkillEvidence");
            m.Entity<SkillEvidence>().HasRequired(x=>x.Request).WithMany().HasForeignKey(x=>x.SkillEvidenceRequestId).WillCascadeOnDelete(false);
            m.Entity<SkillEvidence>().HasOptional(x=>x.Attempt).WithMany().HasForeignKey(x=>x.SkillExamAttemptId).WillCascadeOnDelete(false);
            m.Entity<SkillEvidence>().HasOptional(x=>x.Portfolio).WithMany().HasForeignKey(x=>x.PortfolioItemId).WillCascadeOnDelete(false);
            m.Entity<NegotiationMessage>().ToTable("NegotiationMessages");
            m.Entity<NegotiationMessage>().HasRequired(x => x.Proposal).WithMany().HasForeignKey(x => x.ProposalId).WillCascadeOnDelete(false);
            m.Entity<NegotiationMessage>().HasRequired(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).WillCascadeOnDelete(false);
            m.Entity<NegotiationMessage>().HasIndex(x => new { x.ProposalId, x.Id });
            m.Entity<ClientProfile>().ToTable("ClientProfiles");
            m.Entity<ClientProfile>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<ClientProfile>().HasIndex(x => x.UserAccountId).IsUnique();
            m.Entity<ClientReview>().ToTable("ClientReviews");
            m.Entity<ClientReview>().HasRequired(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).WillCascadeOnDelete(false);
            m.Entity<ClientReview>().HasRequired(x => x.Freelancer).WithMany().HasForeignKey(x => x.FreelancerId).WillCascadeOnDelete(false);
            m.Entity<ClientReview>().HasRequired(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<ClientReview>().HasIndex(x => new { x.ProjectId, x.FreelancerId }).IsUnique();
            m.Entity<RegistrationCode>().ToTable("RegistrationCodes");
            m.Entity<RegistrationCode>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<RegistrationCode>().HasIndex(x => new { x.UserAccountId, x.Id });
            m.Entity<WalletAccount>().ToTable("WalletAccounts");
            m.Entity<WalletAccount>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<WalletAccount>().HasIndex(x => x.UserAccountId).IsUnique();
            m.Entity<WalletEntry>().ToTable("WalletEntries");
            m.Entity<WalletEntry>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<WalletEntry>().HasOptional(x => x.Milestone).WithMany().HasForeignKey(x => x.EscrowMilestoneId).WillCascadeOnDelete(false);
            m.Entity<WalletEntry>().HasIndex(x => x.Reference).IsUnique();
            m.Entity<WalletEntry>().HasIndex(x => new { x.UserAccountId, x.Id });
            m.Entity<MoneyRequest>().ToTable("MoneyRequests");
            m.Entity<MoneyRequest>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<MoneyRequest>().HasOptional(x => x.Reviewer).WithMany().HasForeignKey(x => x.ReviewerId).WillCascadeOnDelete(false);
            m.Entity<MoneyRequest>().HasIndex(x => x.Reference).IsUnique();
            m.Entity<EscrowMilestone>().ToTable("EscrowMilestones");
            m.Entity<EscrowMilestone>().HasRequired(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<EscrowShare>().ToTable("EscrowShares");
            m.Entity<EscrowShare>().HasRequired(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<EscrowShare>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<EscrowShare>().HasIndex(x => new { x.ProjectId, x.UserAccountId });
            m.Entity<EscrowDispute>().ToTable("EscrowDisputes");
            m.Entity<EscrowDispute>().HasRequired(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<EscrowDispute>().HasRequired(x => x.Reporter).WithMany().HasForeignKey(x => x.ReporterId).WillCascadeOnDelete(false);
            m.Entity<EscrowDispute>().HasOptional(x => x.Reviewer).WithMany().HasForeignKey(x => x.ReviewerId).WillCascadeOnDelete(false);
            m.Entity<Deliverable>().HasOptional(x => x.EscrowMilestone).WithMany().HasForeignKey(x => x.EscrowMilestoneId).WillCascadeOnDelete(false);
            m.Entity<Project>().Property(x => x.PlatformFeeRate).HasPrecision(6,4);
            m.Entity<UserAccount>().ToTable("Users");
            m.Entity<PasswordResetToken>().ToTable("PasswordResetTokens");
            m.Entity<PasswordResetToken>().HasRequired(x => x.User).WithMany()
                .HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<PasswordResetToken>().HasIndex(x => x.TokenHash).IsUnique();
            m.Entity<PasswordResetToken>().HasIndex(x => new { x.UserAccountId, x.CreatedAtUtc });
            m.Entity<EmailVerificationToken>().ToTable("EmailVerificationTokens");
            m.Entity<EmailVerificationToken>().HasRequired(x => x.User).WithMany()
                .HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<EmailVerificationToken>().HasIndex(x => x.TokenHash).IsUnique();
            m.Entity<EmailVerificationToken>().HasIndex(x => new { x.UserAccountId, x.CreatedAtUtc });
            m.Entity<AdminAuditLog>().ToTable("AdminAuditLogs");
            m.Entity<AdminAuditLog>().HasRequired(x => x.Actor).WithMany()
                .HasForeignKey(x => x.ActorId).WillCascadeOnDelete(false);
            m.Entity<AdminAuditLog>().HasRequired(x => x.TargetUser).WithMany()
                .HasForeignKey(x => x.TargetUserId).WillCascadeOnDelete(false);
            m.Entity<AdminAuditLog>().HasIndex(x => new { x.TargetUserId, x.CreatedAtUtc });
            m.Entity<ProjectAbuseReport>().ToTable("ProjectAbuseReports");
            m.Entity<ProjectAbuseReport>().HasRequired(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<ProjectAbuseReport>().HasRequired(x => x.Reporter).WithMany()
                .HasForeignKey(x => x.ReporterId).WillCascadeOnDelete(false);
            m.Entity<ProjectAbuseReport>().HasOptional(x => x.Reviewer).WithMany()
                .HasForeignKey(x => x.ReviewerId).WillCascadeOnDelete(false);
            m.Entity<ProjectAbuseReport>().HasIndex(x => new { x.Status, x.Id });
            m.Entity<ProjectAbuseReport>().HasIndex(x => new { x.ReporterId, x.ProjectId });
            m.Entity<FreelancerGroup>().ToTable("FreelancerGroups");
            m.Entity<FreelancerGroupMember>().ToTable("FreelancerGroupMembers");
            m.Entity<FreelancerGroupEvent>().ToTable("FreelancerGroupEvents");
            m.Entity<FreelancerGroup>().HasRequired(x => x.Leader).WithMany()
                .HasForeignKey(x => x.LeaderId).WillCascadeOnDelete(false);
            m.Entity<FreelancerGroupMember>().HasRequired(x => x.Group).WithMany()
                .HasForeignKey(x => x.FreelancerGroupId).WillCascadeOnDelete(false);
            m.Entity<FreelancerGroupMember>().HasRequired(x => x.User).WithMany()
                .HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<FreelancerGroupMember>().HasIndex(x => new { x.FreelancerGroupId, x.UserAccountId }).IsUnique();
            m.Entity<FreelancerGroupEvent>().HasRequired(x => x.Group).WithMany()
                .HasForeignKey(x => x.FreelancerGroupId).WillCascadeOnDelete(false);
            m.Entity<FreelancerGroupEvent>().HasRequired(x => x.Actor).WithMany()
                .HasForeignKey(x => x.ActorId).WillCascadeOnDelete(false);
            m.Entity<GroupBid>().ToTable("GroupBids");
            m.Entity<GroupBid>().HasRequired(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<GroupBid>().HasRequired(x => x.Group).WithMany()
                .HasForeignKey(x => x.FreelancerGroupId).WillCascadeOnDelete(false);
            m.Entity<GroupBid>().HasRequired(x => x.Leader).WithMany()
                .HasForeignKey(x => x.LeaderId).WillCascadeOnDelete(false);
            m.Entity<GroupBid>().HasIndex(x => new { x.ProjectId, x.FreelancerGroupId });
            m.Entity<UserNotification>().ToTable("Notifications");
            m.Entity<UserNotification>().HasRequired(n => n.Recipient).WithMany()
                .HasForeignKey(n => n.RecipientId).WillCascadeOnDelete(false);
            m.Entity<UserNotification>().HasIndex(n => new { n.RecipientId, n.Id });
            m.Entity<Project>().ToTable("Projects");
            m.Entity<RoleRequirement>().ToTable("RoleRequirements");
            m.Entity<Proposal>().ToTable("Proposals");
            m.Entity<RoleAssignment>().ToTable("RoleAssignments");
            m.Entity<ProjectTeam>().ToTable("ProjectTeams");
            m.Entity<TeamMember>().ToTable("TeamMembers");
            m.Entity<Workspace>().ToTable("Workspaces");
            m.Entity<WorkTask>().ToTable("Tasks");
            m.Entity<WorkspaceMessage>().ToTable("WorkspaceMessages");
            m.Entity<ProjectMilestone>().ToTable("ProjectMilestones");
            m.Entity<WorkspaceMeeting>().ToTable("WorkspaceMeetings");
            m.Entity<ProjectMemory>().ToTable("ProjectMemories");
            m.Entity<ContributionRecord>().ToTable("ContributionRecords");
            m.Entity<XPTransaction>().ToTable("XPTransactions");
            m.Entity<SkillDNAHistory>().ToTable("SkillDNAHistories");
            m.Entity<Deliverable>().ToTable("Deliverables");
            m.Entity<Review>().ToTable("Reviews");
            m.Entity<ActivityLog>().ToTable("ActivityLogs");
            m.Entity<AIJobDraft>().ToTable("AIJobDrafts");
            m.Entity<AIJobDraftVersion>().ToTable("AIJobDraftVersions");
            m.Entity<AIJobRoleSuggestion>().ToTable("AIJobRoleSuggestions");
            m.Entity<FreelancerProfile>().ToTable("FreelancerProfiles");
            m.Entity<PortfolioItem>().ToTable("PortfolioItems");
            m.Entity<MatchingResult>().ToTable("MatchingResults");
            m.Entity<SkillTrack>().ToTable("SkillTracks");
            m.Entity<SkillExamQuestion>().ToTable("SkillExamQuestions");
            m.Entity<SkillExamAttempt>().ToTable("SkillExamAttempts");
            m.Entity<SkillExamSession>().ToTable("SkillExamSessions");
            m.Entity<SkillExamSession>().HasRequired(s => s.User).WithMany()
                .HasForeignKey(s => s.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<SkillExamSession>().HasRequired(s => s.SkillTrack).WithMany()
                .HasForeignKey(s => s.SkillTrackId).WillCascadeOnDelete(false);
            m.Entity<SkillExamSession>().HasIndex(s => new { s.UserAccountId, s.SkillTrackId }).IsUnique();
            m.Entity<SkillExamSession>().Property(s => s.StartedAtUtc).HasColumnType("datetime2");
            m.Entity<SkillExamSession>().Property(s => s.ExpiresAtUtc).HasColumnType("datetime2");
            m.Entity<SkillExamSession>().Property(s => s.CompletedAtUtc).HasColumnType("datetime2");
            m.Entity<SkillExamAnswer>().ToTable("SkillExamAnswers");
            m.Entity<EarnedSkillBadge>().ToTable("EarnedSkillBadges");
            m.Entity<EarnedSkillBadge>().HasOptional(b => b.RevokedBy).WithMany()
                .HasForeignKey(b => b.RevokedById).WillCascadeOnDelete(false);
            m.Entity<EarnedSkillBadge>().HasIndex(b => new { b.RevokedAtUtc, b.Id });
            m.Entity<ExternalCertification>().ToTable("ExternalCertifications");
            m.Entity<ExternalCertification>().HasRequired(c => c.User).WithMany()
                .HasForeignKey(c => c.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<ExternalCertification>().HasOptional(c => c.ReviewedBy).WithMany()
                .HasForeignKey(c => c.ReviewedById).WillCascadeOnDelete(false);
            m.Entity<ExternalCertification>().HasIndex(c => new { c.Status, c.Id });
            m.Entity<ExternalCertification>().HasIndex(c => new { c.UserAccountId, c.Id });
            m.Entity<SkillTrack>().HasIndex(t => t.Code).IsUnique();
            m.Entity<SkillExamQuestion>().HasRequired(q => q.SkillTrack).WithMany(t => t.Questions)
                .HasForeignKey(q => q.SkillTrackId).WillCascadeOnDelete(false);
            m.Entity<SkillExamQuestion>().HasIndex(q => new { q.SkillTrackId, q.Ordinal }).IsUnique();
            m.Entity<SkillExamAttempt>().HasRequired(a => a.User).WithMany()
                .HasForeignKey(a => a.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<SkillExamAttempt>().HasRequired(a => a.SkillTrack).WithMany()
                .HasForeignKey(a => a.SkillTrackId).WillCascadeOnDelete(false);
            m.Entity<SkillExamAnswer>().HasRequired(a => a.Attempt).WithMany(t => t.Answers)
                .HasForeignKey(a => a.SkillExamAttemptId).WillCascadeOnDelete(false);
            m.Entity<SkillExamAnswer>().HasRequired(a => a.Question).WithMany()
                .HasForeignKey(a => a.SkillExamQuestionId).WillCascadeOnDelete(false);
            m.Entity<SkillExamAnswer>().HasIndex(a => new { a.SkillExamAttemptId, a.SkillExamQuestionId }).IsUnique();
            m.Entity<EarnedSkillBadge>().HasRequired(b => b.User).WithMany()
                .HasForeignKey(b => b.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<EarnedSkillBadge>().HasRequired(b => b.SkillTrack).WithMany()
                .HasForeignKey(b => b.SkillTrackId).WillCascadeOnDelete(false);
            m.Entity<EarnedSkillBadge>().HasRequired(b => b.EvidenceAttempt).WithMany()
                .HasForeignKey(b => b.EvidenceAttemptId).WillCascadeOnDelete(false);
            m.Entity<EarnedSkillBadge>().HasIndex(b => new { b.UserAccountId, b.SkillTrackId }).IsUnique();
            m.Entity<EarnedSkillBadge>().HasIndex(b => b.EvidenceAttemptId).IsUnique();
            m.Entity<FreelancerProfile>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<FreelancerProfile>().HasIndex(x => x.UserAccountId).IsUnique();
            m.Entity<PortfolioItem>().HasOptional(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<PortfolioItem>().HasRequired(x => x.Freelancer).WithMany().HasForeignKey(x => x.FreelancerId).WillCascadeOnDelete(false);
            m.Entity<MatchingResult>().HasRequired(x => x.Proposal).WithMany().HasForeignKey(x => x.ProposalId).WillCascadeOnDelete(false);
            m.Entity<MatchingResult>().HasIndex(x => x.ProposalId).IsUnique();
            m.Entity<AIJobDraft>().HasRequired(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).WillCascadeOnDelete(false);
            m.Entity<AIJobDraft>().HasOptional(x => x.AppliedProject).WithMany().HasForeignKey(x => x.AppliedProjectId).WillCascadeOnDelete(false);
            m.Entity<AIJobDraftVersion>().HasRequired(x => x.Draft).WithMany(x => x.Versions).HasForeignKey(x => x.AIJobDraftId).WillCascadeOnDelete(false);
            m.Entity<AIJobDraftVersion>().HasIndex(x => new { x.AIJobDraftId, x.VersionNumber }).IsUnique();
            m.Entity<AIJobRoleSuggestion>().HasRequired(x => x.Version).WithMany(x => x.Roles).HasForeignKey(x => x.AIJobDraftVersionId).WillCascadeOnDelete(false);
            m.Entity<AIJobRoleSuggestion>().HasIndex(x => new { x.AIJobDraftVersionId, x.Title }).IsUnique();
            m.Entity<AIJobDraft>().Property(x => x.Budget).HasPrecision(18, 2);
            m.Entity<UserAccount>().HasIndex(x => new { x.Email, x.Role }).IsUnique();
            m.Entity<Project>().HasRequired(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).WillCascadeOnDelete(false);
            m.Entity<RoleRequirement>().HasRequired(x => x.Project).WithMany(x => x.Requirements).HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<RoleRequirement>().HasIndex(x => new { x.ProjectId, x.Title }).IsUnique();
            m.Entity<Proposal>().HasRequired(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<Proposal>().HasRequired(x => x.Requirement).WithMany().HasForeignKey(x => x.RoleRequirementId).WillCascadeOnDelete(false);
            m.Entity<Proposal>().HasRequired(x => x.Applicant).WithMany().HasForeignKey(x => x.ApplicantId).WillCascadeOnDelete(false);
            m.Entity<Proposal>().HasOptional(x => x.GroupBid).WithMany().HasForeignKey(x => x.GroupBidId).WillCascadeOnDelete(false);
            m.Entity<Proposal>().HasIndex(x => new { x.RoleRequirementId, x.ApplicantId }).IsUnique();
            m.Entity<RoleAssignment>().HasRequired(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<RoleAssignment>().HasRequired(x => x.Requirement).WithMany().HasForeignKey(x => x.RoleRequirementId).WillCascadeOnDelete(false);
            m.Entity<RoleAssignment>().HasRequired(x => x.Freelancer).WithMany().HasForeignKey(x => x.FreelancerId).WillCascadeOnDelete(false);
            m.Entity<RoleAssignment>().HasRequired(x => x.Proposal).WithMany().HasForeignKey(x => x.ProposalId).WillCascadeOnDelete(false);
            m.Entity<RoleAssignment>().HasIndex(x => x.ProposalId).IsUnique();
            m.Entity<RoleAssignment>().HasIndex(x => new { x.ProjectId, x.FreelancerId }).IsUnique();
            m.Entity<ProjectTeam>().HasRequired(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<ProjectTeam>().HasIndex(x => x.ProjectId).IsUnique();
            m.Entity<TeamMember>().HasRequired(x => x.Team).WithMany(x => x.Members).HasForeignKey(x => x.ProjectTeamId).WillCascadeOnDelete(false);
            m.Entity<TeamMember>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<TeamMember>().HasRequired(x => x.Assignment).WithMany().HasForeignKey(x => x.RoleAssignmentId).WillCascadeOnDelete(false);
            m.Entity<TeamMember>().HasIndex(x => x.RoleAssignmentId).IsUnique();
            m.Entity<TeamMember>().HasIndex(x => new { x.ProjectTeamId, x.UserAccountId }).IsUnique();
            m.Entity<Workspace>().HasRequired(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<Workspace>().HasRequired(x => x.Team).WithMany().HasForeignKey(x => x.ProjectTeamId).WillCascadeOnDelete(false);
            m.Entity<Workspace>().HasIndex(x => x.ProjectId);
            m.Entity<Workspace>().HasIndex(x => x.ProjectTeamId);
            m.Entity<WorkTask>().HasRequired(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).WillCascadeOnDelete(false);
            m.Entity<WorkTask>().HasRequired(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeId).WillCascadeOnDelete(false);
            m.Entity<TaskHistory>().ToTable("TaskHistories");
            m.Entity<TaskHistory>().HasRequired(x => x.Task).WithMany().HasForeignKey(x => x.WorkTaskId).WillCascadeOnDelete(false);
            m.Entity<TaskHistory>().HasRequired(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).WillCascadeOnDelete(false);
            m.Entity<TaskHistory>().HasIndex(x => new { x.WorkTaskId, x.Id });
            m.Entity<WorkspaceMessage>().HasRequired(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).WillCascadeOnDelete(false);
            m.Entity<WorkspaceMessage>().HasRequired(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).WillCascadeOnDelete(false);
            m.Entity<WorkspaceMessage>().HasIndex(x => new { x.WorkspaceId, x.Id });
            m.Entity<ProjectMilestone>().HasRequired(x => x.Workspace).WithMany()
                .HasForeignKey(x => x.WorkspaceId).WillCascadeOnDelete(false);
            m.Entity<ProjectMilestone>().HasIndex(x => new { x.WorkspaceId, x.DueAtUtc });
            m.Entity<WorkspaceMeeting>().HasRequired(x => x.Workspace).WithMany()
                .HasForeignKey(x => x.WorkspaceId).WillCascadeOnDelete(false);
            m.Entity<WorkspaceMeeting>().HasIndex(x => new { x.WorkspaceId, x.StartsAtUtc });
            m.Entity<ProjectMemory>().HasRequired(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<ProjectMemory>().HasRequired(x => x.Editor).WithMany().HasForeignKey(x => x.EditorId).WillCascadeOnDelete(false);
            m.Entity<ProjectMemory>().HasIndex(x => new { x.ProjectId, x.Version }).IsUnique();
            m.Entity<ContributionRecord>().HasRequired(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<ContributionRecord>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<ContributionRecord>().HasRequired(x => x.Review).WithMany().HasForeignKey(x => x.ReviewId).WillCascadeOnDelete(false);
            m.Entity<ContributionRecord>().HasIndex(x => new { x.ProjectId, x.UserAccountId }).IsUnique();
            m.Entity<ContributionRecord>().HasIndex(x => x.ReviewId).IsUnique();
            m.Entity<XPTransaction>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<XPTransaction>().HasRequired(x => x.Contribution).WithMany().HasForeignKey(x => x.ContributionRecordId).WillCascadeOnDelete(false);
            m.Entity<XPTransaction>().HasIndex(x => x.ContributionRecordId).IsUnique();
            m.Entity<SkillDNAHistory>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<SkillDNAHistory>().HasRequired(x => x.Contribution).WithMany().HasForeignKey(x => x.ContributionRecordId).WillCascadeOnDelete(false);
            m.Entity<SkillDNAHistory>().HasIndex(x => x.ContributionRecordId).IsUnique();
            m.Entity<Deliverable>().HasRequired(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).WillCascadeOnDelete(false);
            m.Entity<Deliverable>().HasRequired(x => x.SubmittedBy).WithMany().HasForeignKey(x => x.SubmittedById).WillCascadeOnDelete(false);
            m.Entity<Deliverable>().HasOptional(x => x.RevisedFrom).WithMany().HasForeignKey(x => x.RevisesId).WillCascadeOnDelete(false);
            m.Entity<WorkspaceFile>().ToTable("WorkspaceFiles");
            m.Entity<WorkspaceFileVersion>().ToTable("WorkspaceFileVersions");
            m.Entity<WorkspaceFile>().HasRequired(f => f.Workspace).WithMany()
                .HasForeignKey(f => f.WorkspaceId).WillCascadeOnDelete(false);
            m.Entity<WorkspaceFile>().HasRequired(f => f.CreatedBy).WithMany()
                .HasForeignKey(f => f.CreatedById).WillCascadeOnDelete(false);
            m.Entity<WorkspaceFile>().HasIndex(f => new { f.WorkspaceId, f.Id });
            m.Entity<WorkspaceFileVersion>().HasRequired(v => v.File).WithMany()
                .HasForeignKey(v => v.WorkspaceFileId).WillCascadeOnDelete(false);
            m.Entity<WorkspaceFileVersion>().HasRequired(v => v.UploadedBy).WithMany()
                .HasForeignKey(v => v.UploadedById).WillCascadeOnDelete(false);
            m.Entity<WorkspaceFileVersion>().HasIndex(v => new { v.WorkspaceFileId, v.VersionNumber }).IsUnique();
            m.Entity<Review>().HasRequired(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<Review>().HasRequired(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).WillCascadeOnDelete(false);
            m.Entity<Review>().HasRequired(x => x.Freelancer).WithMany().HasForeignKey(x => x.FreelancerId).WillCascadeOnDelete(false);
            m.Entity<Review>().HasIndex(x => new { x.ProjectId, x.FreelancerId }).IsUnique();
            m.Entity<ActivityLog>().HasRequired(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<ActivityLog>().HasRequired(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).WillCascadeOnDelete(false);
            m.Entity<Project>().Property(x => x.Budget).HasPrecision(18, 2);
            m.Entity<Proposal>().Property(x => x.QuotedPrice).HasPrecision(18, 2);
        }
    }
}
