using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.ViewModels
{
    public class AdminDashboardPage
    {
        public int UserCount { get; set; }
        public int SuspendedCount { get; set; }
        public int ProjectCount { get; set; }
        public int AuditCount { get; set; }
    }

    public class AdminUserRow
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public bool IsSuspended { get; set; }
        public DateTime JoinedAtUtc { get; set; }
    }

    public class AdminUsersPage
    {
        public string Query { get; set; }
        public int Page { get; set; }
        public int PageCount { get; set; }
        public int Total { get; set; }
        public IList<AdminUserRow> Users { get; set; } = new List<AdminUserRow>();
    }

    public class AdminAuditRow
    {
        public int Id { get; set; }
        public int ActorId { get; set; }
        public int TargetUserId { get; set; }
        public string Actor { get; set; }
        public string Target { get; set; }
        public string Action { get; set; }
        public string Reason { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class AdminAuditPage
    {
        public string Query { get; set; }
        public string Action { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public int? UserId { get; set; }
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageCount { get; set; }
        public IList<AdminAuditRow> Logs { get; set; } = new List<AdminAuditRow>();
    }

    // Explicit allowlist: never pass UserAccount or financial/authentication records to this view.
    public class AdminProfilePage
    {
        public AdminUserRow User { get; set; }
        public string CoreRole { get; set; }
        public string Bio { get; set; }
        public string Industry { get; set; }
        public string Location { get; set; }
        public int? YearsExperience { get; set; }
        public decimal? MinimumProjectBudget { get; set; }
        public bool? IsAvailable { get; set; }
        public bool? IsPublic { get; set; }
        public string ClientType { get; set; }
        public string CompanyName { get; set; }
        public string Website { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public string SelectedRoles { get; set; }
        public IList<VerifiedSkillRow> VerifiedSkills { get; set; } = new List<VerifiedSkillRow>();
        public IList<AdminReviewRow> Reviews { get; set; } = new List<AdminReviewRow>();
        public IList<AdminActivityRow> Events { get; set; } = new List<AdminActivityRow>();
        public IList<AdminCertificateRow> Certificates { get; set; } = new List<AdminCertificateRow>();
        public IList<AdminPortfolioRow> Portfolio { get; set; } = new List<AdminPortfolioRow>();
    }

    public class AdminReviewRow
    {
        public int Id { get; set; }
        public int ReviewerId { get; set; }
        public string Reviewer { get; set; }
        public string Project { get; set; }
        public int Rating { get; set; }
        public string Comment { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class AdminCertificateRow
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Issuer { get; set; }
        public string Status { get; set; }
        public DateTime IssuedOn { get; set; }
        public DateTime? ExpiresOn { get; set; }
        public DateTime SubmittedAtUtc { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
        public string ReviewReason { get; set; }
        public int? ReviewerId { get; set; }
    }

    public class AdminPortfolioRow
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Summary { get; set; }
        public string Role { get; set; }
        public string Skills { get; set; }
        public string Outcome { get; set; }
        public string Url { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class AdminActivityRow
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public string Summary { get; set; }
        public DateTime AtUtc { get; set; }
    }

    public class AdminAccessRow
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Controller { get; set; }
        public string Action { get; set; }
        public DateTime AccessedAtUtc { get; set; }
    }

    public class AdminAccessPage
    {
        public string Query { get; set; }
        public string Controller { get; set; }
        public string Action { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public int? UserId { get; set; }
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageCount { get; set; }
        public IList<AdminAccessRow> Logs { get; set; } = new List<AdminAccessRow>();
    }

    public class AdminChangeStatusForm
    {
        [Range(1, int.MaxValue)] public int Id { get; set; }
        public bool Suspend { get; set; }
        [Required, StringLength(200, MinimumLength = 5)] public string Reason { get; set; }
    }

    public class AdminBadgesPage
    {
        public string Query { get; set; }
        public string Status { get; set; }
        public int Page { get; set; }
        public int PageCount { get; set; }
        public int Total { get; set; }
        public IList<NexusFreelance.Web.Models.EarnedSkillBadge> Badges { get; set; } =
            new List<NexusFreelance.Web.Models.EarnedSkillBadge>();
    }

    public class AdminRevokeBadgeForm
    {
        [Range(1, int.MaxValue)] public int BadgeId { get; set; }
        [Required, StringLength(170, MinimumLength = 5)] public string Reason { get; set; }
    }
}
