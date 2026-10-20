using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Services
{
    public class ActivityInspectionService
    {
        private readonly AppDbContext db;
        public ActivityInspectionService(AppDbContext db) { this.db = db; }

        public AdminProfilePage Profile(int id)
        {
            var user = db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new AdminUserRow
            {
                Id = u.Id, Name = u.Name, Email = u.Email, Role = u.Role,
                IsSuspended = u.IsSuspended, JoinedAtUtc = u.JoinedAtUtc
            }).SingleOrDefault();
            if (user == null) return null;
            var page = new AdminProfilePage { User = user };
            // No public-directory visibility predicate: this is an authorized support inspection.
            if (user.Role == "Freelancer")
            {
                var p = db.FreelancerProfiles.AsNoTracking().SingleOrDefault(x => x.UserAccountId == id);
                if (p != null)
                {
                    page.CoreRole = p.CoreRole; page.Bio = p.Bio; page.Industry = p.Industry;
                    page.Location = p.Location; page.YearsExperience = p.YearsExperience;
                    page.MinimumProjectBudget = p.MinimumProjectBudget; page.IsAvailable = p.IsAvailable;
                    page.IsPublic = p.IsPublic != false; page.UpdatedAtUtc = p.UpdatedAtUtc;
                    var matching = new SmartMatchingService(db);
                    var evidence = matching.Evidence(new[] { id });
                    page.VerifiedSkills = evidence[id].Take(60).ToList();
                    page.SelectedRoles = matching.SelectedRoleLabels(new[] { p }, evidence)[id];
                }
                page.Reviews = db.Reviews.AsNoTracking().Where(x => x.FreelancerId == id)
                    .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(20)
                    .Select(x => new AdminReviewRow { Id = x.Id, ReviewerId = x.ClientId, Reviewer = x.Client.Name,
                        Project = x.Project.Title, Rating = x.Rating, Comment = x.Comment, CreatedAtUtc = x.CreatedAtUtc }).ToList();
            }
            if (user.Role == "Client")
            {
                var p = db.ClientProfiles.AsNoTracking().SingleOrDefault(x => x.UserAccountId == id);
                if (p != null)
                {
                    page.ClientType = p.ClientType; page.CompanyName = p.CompanyName;
                    page.Bio = p.About; page.Location = p.Location; page.Website = p.Website;
                    page.UpdatedAtUtc = p.UpdatedAtUtc;
                }
                page.Reviews = db.ClientReviews.AsNoTracking().Where(x => x.ClientId == id)
                    .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(20)
                    .Select(x => new AdminReviewRow { Id = x.Id, ReviewerId = x.FreelancerId, Reviewer = x.Freelancer.Name,
                        Project = x.Project.Title, Rating = x.Rating, Comment = x.Comment, CreatedAtUtc = x.CreatedAtUtc }).ToList();
            }
            page.Portfolio = db.PortfolioItems.AsNoTracking().Where(x => x.FreelancerId == id)
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(20)
                .Select(x => new AdminPortfolioRow { Id = x.Id, Title = x.Title, Summary = x.Summary,
                    Role = x.Role, Skills = x.Skills, Outcome = x.Outcome, Url = x.Url, CreatedAtUtc = x.CreatedAtUtc }).ToList();
            page.Certificates = db.ExternalCertifications.AsNoTracking().Where(x => x.UserAccountId == id)
                .OrderByDescending(x => x.SubmittedAtUtc).ThenByDescending(x => x.Id).Take(20)
                .Select(x => new AdminCertificateRow { Id = x.Id, Name = x.Name, Issuer = x.Issuer,
                    Status = x.Status, IssuedOn = x.IssuedOn, ExpiresOn = x.ExpiresOn,
                    SubmittedAtUtc = x.SubmittedAtUtc, ReviewedAtUtc = x.ReviewedAtUtc,
                    ReviewerId = x.ReviewedById, ReviewReason = x.ReviewReason }).ToList();
            page.Events = Recent(id);
            return page;
        }

        public IList<AdminActivityRow> Recent(int id)
        {
            const int limit = 60;
            var events = new List<AdminActivityRow>();
            events.AddRange(db.ExternalCertifications.AsNoTracking().Where(x => x.UserAccountId == id)
                .OrderByDescending(x => x.SubmittedAtUtc).ThenByDescending(x => x.Id).Take(limit)
                .Select(x => new AdminActivityRow { Id = x.Id, Type = "CertificateSubmitted", AtUtc = x.SubmittedAtUtc, Summary = x.Name + " · " + x.Issuer }).ToList());
            events.AddRange(db.ExternalCertifications.AsNoTracking().Where(x => x.UserAccountId == id && x.ReviewedAtUtc.HasValue)
                .OrderByDescending(x => x.ReviewedAtUtc).ThenByDescending(x => x.Id).Take(limit)
                .Select(x => new AdminActivityRow { Id = x.Id, Type = "CertificateReviewRecorded", AtUtc = x.ReviewedAtUtc.Value, Summary = x.Name + " · " + x.Status + " · " + x.ReviewReason }).ToList());
            events.AddRange(db.SkillExamAttempts.AsNoTracking().Where(x => x.UserAccountId == id && x.StartedAtUtc.HasValue)
                .OrderByDescending(x => x.StartedAtUtc).ThenByDescending(x => x.Id).Take(limit)
                .Select(x => new AdminActivityRow { Id = x.Id, Type = "ExamStarted", AtUtc = x.StartedAtUtc.Value, Summary = x.SkillTrack.Title }).ToList());
            var exams = db.SkillExamAttempts.AsNoTracking().Where(x => x.UserAccountId == id)
                .OrderByDescending(x => x.SubmittedAtUtc).ThenByDescending(x => x.Id).Take(limit)
                .Select(x => new { x.Id, x.SubmittedAtUtc, x.Percent, x.CorrectCount, x.QuestionCount, x.Passed,
                    x.DurationSeconds, x.BrowserFocusLossCount, Title = x.SkillTrack.Title }).ToList();
            events.AddRange(exams.Select(x => new AdminActivityRow { Id = x.Id, Type = "ExamSubmitted", AtUtc = x.SubmittedAtUtc,
                Summary = x.Title + " · " + x.CorrectCount + "/" + x.QuestionCount + " (" + x.Percent + "%) · " + (x.Passed ? "Đạt" : "Chưa đạt") +
                    " · Thời lượng: " + (x.DurationSeconds.HasValue ? x.DurationSeconds + " giây" : "chưa ghi nhận") +
                    " · Mất tiêu điểm (trình duyệt tự báo, không kết luận vi phạm): " + (x.BrowserFocusLossCount.HasValue ? x.BrowserFocusLossCount.ToString() : "chưa ghi nhận") }));
            events.AddRange(db.EarnedSkillBadges.AsNoTracking().Where(x => x.UserAccountId == id)
                .OrderByDescending(x => x.IssuedAtUtc).ThenByDescending(x => x.Id).Take(limit)
                .Select(x => new AdminActivityRow { Id = x.Id, Type = "BadgeIssued", AtUtc = x.IssuedAtUtc, Summary = x.SkillTrack.Title }).ToList());
            events.AddRange(db.EarnedSkillBadges.AsNoTracking().Where(x => x.UserAccountId == id && x.RevokedAtUtc.HasValue)
                .OrderByDescending(x => x.RevokedAtUtc).ThenByDescending(x => x.Id).Take(limit)
                .Select(x => new AdminActivityRow { Id = x.Id, Type = "BadgeRevoked", AtUtc = x.RevokedAtUtc.Value, Summary = x.SkillTrack.Title + " · " + x.RevocationReason }).ToList());
            events.AddRange(db.Projects.AsNoTracking().Where(x => x.ClientId == id)
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(limit)
                .Select(x => new AdminActivityRow { Id = x.Id, Type = "ProjectCreated", AtUtc = x.CreatedAtUtc, Summary = x.Title }).ToList());
            events.AddRange(db.Proposals.AsNoTracking().Where(x => x.ApplicantId == id)
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(limit)
                .Select(x => new AdminActivityRow { Id = x.Id, Type = "ProposalCreated", AtUtc = x.CreatedAtUtc, Summary = x.Project.Title + " · " + x.Requirement.Title }).ToList());
            events.AddRange(db.PortfolioItems.AsNoTracking().Where(x => x.FreelancerId == id)
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(limit)
                .Select(x => new AdminActivityRow { Id = x.Id, Type = "PortfolioCreated", AtUtc = x.CreatedAtUtc, Summary = x.Title }).ToList());
            events.AddRange(db.AdminAuditLogs.AsNoTracking().Where(x => x.ActorId == id || x.TargetUserId == id)
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(limit)
                .Select(x => new AdminActivityRow { Id = x.Id, Type = "ModerationRecorded", AtUtc = x.CreatedAtUtc, Summary = x.Action + " · " + x.Reason }).ToList());
            events.AddRange(db.ProjectAbuseReports.AsNoTracking().Where(x => x.ReporterId == id)
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(limit)
                .Select(x => new AdminActivityRow { Id = x.Id, Type = "ProjectReportSubmitted", AtUtc = x.CreatedAtUtc, Summary = x.Project.Title + " · " + x.Category }).ToList());
            events.AddRange(db.ProjectAbuseReports.AsNoTracking().Where(x => x.ReviewedAtUtc.HasValue && (x.ReporterId == id || x.ReviewerId == id || x.Project.ClientId == id))
                .OrderByDescending(x => x.ReviewedAtUtc).ThenByDescending(x => x.Id).Take(limit)
                .Select(x => new AdminActivityRow { Id = x.Id, Type = "ProjectReportReviewRecorded", AtUtc = x.ReviewedAtUtc.Value, Summary = x.Project.Title + " · " + x.Status + " · " + x.Resolution }).ToList());
            // This is the existing immutable project event, not a reconstructed status transition.
            events.AddRange(db.ActivityLogs.AsNoTracking().Where(x => x.ActorId == id)
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(limit)
                .Select(x => new AdminActivityRow { Id = x.Id, Type = "ProjectActivityRecorded", AtUtc = x.CreatedAtUtc, Summary = x.Project.Title + " · " + x.Action }).ToList());
            return events.OrderByDescending(x => x.AtUtc).ThenBy(x => x.Type).ThenByDescending(x => x.Id).Take(limit).ToList();
        }
    }
}
