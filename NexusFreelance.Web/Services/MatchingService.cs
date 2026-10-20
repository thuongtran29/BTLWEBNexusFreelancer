using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Services
{
    // Deterministic, explainable MVP scoring. All components are snapshots at proposal submission.
    public sealed class MatchingService
    {
        private readonly AppDbContext db;
        public MatchingService(AppDbContext db) { this.db = db; }

        private static readonly HashSet<string> GenericWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "developer", "engineer", "dev", "software", "specialist", "chuyen", "gia", "nhan", "vien",
            "lap", "trinh", "phat", "trien", "ky", "su", "and", "the", "for"
        };

        private static string Normalize(string value)
        {
            string expanded = (value ?? "").ToLowerInvariant().Replace('đ', 'd');
            expanded = expanded.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(expanded.Length);
            foreach (var c in expanded)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
            return builder.ToString().Normalize(NormalizationForm.FormC)
                .Replace("front-end", "frontend").Replace("front end", "frontend")
                .Replace("back-end", "backend").Replace("back end", "backend");
        }

        private static HashSet<string> Keywords(string value)
        {
            return new HashSet<string>(Regex.Matches(Normalize(value), @"[a-z0-9#+]+")
                .Cast<Match>().Select(m => m.Value)
                .Where(s => s.Length >= 2 && !GenericWords.Contains(s)), StringComparer.Ordinal);
        }

        public static int VerifiedRolePoints(string requiredTitle, IEnumerable<VerifiedSkillRow> selectedRoles)
        {
            var normalized = Normalize(requiredTitle).Trim();
            if (normalized.Length == 0) return 0;
            var required = Keywords(requiredTitle);
            return selectedRoles.Select(role =>
            {
                if (Normalize(role.Title).Trim() == normalized) return 30;
                var keywords = Keywords(role.Title);
                return required.Count == 0 ? 0 : Math.Min(24, (int)Math.Round(24.0 * required.Intersect(keywords).Count() / required.Count,
                    MidpointRounding.AwayFromZero));
            }).DefaultIfEmpty(0).Max();
        }

        private static string Summarize(IEnumerable<string> descriptions, int limit)
        {
            var all = descriptions.ToList();
            var shown = new List<string>();
            var length = 0;
            foreach (var description in all)
            {
                var nextLength = length + (shown.Count == 0 ? 0 : 2) + description.Length;
                if (nextLength > limit) break;
                shown.Add(description);
                length = nextLength;
            }
            return string.Join(", ", shown) + (shown.Count == all.Count ? "" : " (và " + (all.Count - shown.Count) + " mục đã xác minh khác)");
        }

        public MatchingResult CreateSnapshot(int applicantId, Project project, RoleRequirement requirement, decimal quotedPrice)
        {
            var profile = db.FreelancerProfiles.SingleOrDefault(p => p.UserAccountId == applicantId);
            var portfolio = db.PortfolioItems.Where(p => p.FreelancerId == applicantId).ToList();
            var reviews = db.Reviews.Where(r => r.FreelancerId == applicantId && r.Project.Status == "Completed")
                .Select(r => r.Rating).ToList();
            var evidence = new SmartMatchingService(db).Evidence(new[] { applicantId })[applicantId];
            var verifiedSkillIds = evidence.Select(e => e.SkillId).ToArray();
            var badges = db.SkillTracks.Where(t => verifiedSkillIds.Contains(t.Id)).ToList();
            var secondaryRoles = db.Set<FreelancerSecondaryRole>().Where(r => r.UserAccountId == applicantId).ToList();
            var selectedRoles = SmartMatchingService.SelectedRoles(profile, secondaryRoles, evidence);

            var required = Keywords(requirement.Title);
            int rolePoints = VerifiedRolePoints(requirement.Title, selectedRoles);

            var relevantProjects = required.Count == 0 ? 0 : portfolio.Count(p =>
                required.Overlaps(Keywords(p.Title + " " + p.Summary)));
            int portfolioPoints = Math.Min(15, relevantProjects * 5);
            var relevantBadges = required.Count == 0 ? new List<SkillTrack>() :
                badges.Where(t => required.Overlaps(Keywords(t.MatchKeywords))).ToList();
            // Each distinct relevant verified skill contributes 10 points, capped at 20.
            // Selected roles never restrict ability evidence; disclose its source and level below.
            int badgePoints = Math.Min(20, relevantBadges.Count * 10);
            int availabilityPoints = profile != null && profile.IsAvailable ? 15 : 0;
            int reliabilityPoints = reviews.Count == 0 ? 0 : Math.Min(10, (int)Math.Round(reviews.Average() * 2,
                MidpointRounding.AwayFromZero));

            // Project budget equals a role budget only for a single-person SOLO project.
            bool roleBudgetKnown = project.ProjectType == "SOLO" && requirement.RequiredCount == 1 &&
                db.RoleRequirements.Count(r => r.ProjectId == project.Id) == 1;
            int budgetPoints = roleBudgetKnown && quotedPrice <= project.Budget ? 10 : 0;

            var roleReason = selectedRoles.Count == 0 ? "chưa chọn vai trò chính/phụ có minh chứng còn hiệu lực" :
                (rolePoints == 30 ? "vai trò đã chọn trùng khớp" : "đối chiếu từng vai trò đã chọn theo mức độ bao phủ, lấy điểm cao nhất") + ": " +
                Summarize(selectedRoles.Select(r => (r.SkillId == profile.CoreSkillTrackId ? "chính: " : "phụ: ") + r.Title), 300);
            roleReason += "; chỉ dùng vai trò đã xác minh còn hiệu lực, không tính chức danh tự giới thiệu";
            var budgetReason = !roleBudgetKnown ? "chưa xác định ngân sách riêng từng vị trí" :
                (budgetPoints == 10 ? "báo giá nằm trong ngân sách SOLO" : "báo giá vượt ngân sách SOLO");
            var explanation = string.Join(" | ", new[]
            {
                "Vai trò " + rolePoints + "/30: " + roleReason,
                "Năng lực " + badgePoints + "/20: " + (relevantBadges.Count == 0
                    ? "chưa có chứng chỉ hoặc minh chứng còn hiệu lực phù hợp vị trí"
                    : Summarize(evidence.Where(e => relevantBadges.Any(t => t.Id == e.SkillId))
                        .Select(e => e.Title + " · " + SmartMatchingService.LevelName(e.Level) + " · " + e.Evidence), 400)),
                "Portfolio " + portfolioPoints + "/15: " + relevantProjects + " mục liên quan, tự khai báo/chưa xác minh",
                "Sẵn sàng " + availabilityPoints + "/15: " + (profile == null ? "chưa có hồ sơ" : profile.IsAvailable ? "đang nhận việc" : "chưa nhận việc"),
                "Uy tín " + reliabilityPoints + "/10: " + (reviews.Count == 0 ? "chưa có đánh giá hoàn thành, chưa đủ dữ liệu" : reviews.Count + " đánh giá hoàn thành"),
                "Ngân sách " + budgetPoints + "/10: " + budgetReason
            });

            return new MatchingResult
            {
                CoreRolePoints = rolePoints, BadgePoints = badgePoints, PortfolioPoints = portfolioPoints,
                AvailabilityPoints = availabilityPoints, ReliabilityPoints = reliabilityPoints,
                BudgetPoints = budgetPoints,
                TotalPoints = rolePoints + badgePoints + portfolioPoints + availabilityPoints + reliabilityPoints + budgetPoints,
                Explanation = explanation, CreatedAtUtc = DateTime.UtcNow
            };
        }
    }
}
