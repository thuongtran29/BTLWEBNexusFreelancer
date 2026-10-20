using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Services
{
    // Shared live evidence for discovery, profile summaries and reverse recommendations.
    // Proposal matching remains an immutable snapshot; recommendations never select/sign a bid.
    public sealed class SmartMatchingService
    {
        private readonly AppDbContext db;
        public SmartMatchingService(AppDbContext db) { this.db = db; }
        public static string LevelName(int level) => new[] { "Chưa đánh giá", "Nền tảng", "Thực hành", "Thành thạo", "Nâng cao", "Chuyên sâu" }[Math.Max(0, Math.Min(5, level))];
        public Dictionary<int, List<VerifiedSkillRow>> Evidence(int[] userIds)
        {
            userIds = userIds.Distinct().ToArray();
            if (userIds.Length > 500)
            {
                var batches = new Dictionary<int, List<VerifiedSkillRow>>();
                for (var start = 0; start < userIds.Length; start += 500)
                    foreach (var pair in Evidence(userIds.Skip(start).Take(500).ToArray())) batches.Add(pair.Key, pair.Value);
                return batches;
            }
            var result = userIds.Distinct().ToDictionary(id => id, id => new List<VerifiedSkillRow>());
            var today = DateTime.Today;
            var claims = db.SkillClaims.AsNoTracking().Include(c => c.Skill).Where(c => userIds.Contains(c.UserAccountId) && !c.IsDeleted && c.Skill.IsActive != false &&
                db.SkillEvidenceRequests.Any(r => r.SkillClaimId == c.Id && r.Status == "Approved" && (!r.ValidUntil.HasValue || r.ValidUntil >= today))).ToList();
            foreach (var c in claims) result[c.UserAccountId].Add(new VerifiedSkillRow { SkillId = c.SkillTrackId, Title = c.Skill.Title, Level = c.Level, Evidence = "Minh chứng được quản trị duyệt" });
            var badges = db.EarnedSkillBadges.AsNoTracking().Include(b => b.SkillTrack).Where(b => userIds.Contains(b.UserAccountId) && b.RevokedAtUtc == null && b.SkillTrack.IsActive != false).ToList();
            foreach (var b in badges) result[b.UserAccountId].Add(new VerifiedSkillRow { SkillId = b.SkillTrackId, Title = b.SkillTrack.Title, Level = 1, Evidence = "Chứng chỉ nội bộ · nền tảng" });
            var certs = db.ExternalCertifications.AsNoTracking().Include(c => c.Skill).Where(c => userIds.Contains(c.UserAccountId) && c.Status == "Verified" && c.SkillTrackId.HasValue && c.Skill.IsActive != false && (!c.ExpiresOn.HasValue || c.ExpiresOn >= today)).ToList();
            foreach (var c in certs) result[c.UserAccountId].Add(new VerifiedSkillRow { SkillId = c.SkillTrackId.Value, Title = c.Skill.Title, Level = c.ProficiencyLevel ?? 1, Evidence = "Chứng chỉ ngoài · " + c.Issuer });
            foreach (var id in userIds.Distinct()) result[id] = result[id].GroupBy(s => s.SkillId).Select(g => g.OrderByDescending(s => s.Level).First()).OrderByDescending(s => s.Level).ThenBy(s => s.Title).ToList();
            return result;
        }

        private static HashSet<string> Tokens(string text)
        {
            var plain = new string((text ?? "").ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
            return new HashSet<string>(Regex.Matches(plain.Replace("front-end", "frontend").Replace("back-end", "backend"), @"[a-z0-9+#]+").Cast<Match>().Select(m => m.Value).Where(t => t.Length > 1 && !new[] { "developer", "engineer", "chuyen", "vien", "nhan", "thi", "the", "and" }.Contains(t)));
        }

        // Resolve titles from live evidence, never from the professional title or saved IDs alone.
        public static List<VerifiedSkillRow> SelectedRoles(FreelancerProfile profile, IEnumerable<FreelancerSecondaryRole> secondaryRoles, IEnumerable<VerifiedSkillRow> evidence)
        {
            if (profile == null) return new List<VerifiedSkillRow>();
            var ids = new HashSet<int>(secondaryRoles.Where(r => r.UserAccountId == profile.UserAccountId).Select(r => r.SkillTrackId));
            if (profile.CoreSkillTrackId.HasValue) ids.Add(profile.CoreSkillTrackId.Value);
            return evidence.Where(e => ids.Contains(e.SkillId)).GroupBy(e => e.SkillId).Select(g => g.OrderByDescending(e => e.Level).First())
                .OrderBy(e => e.SkillId == profile.CoreSkillTrackId ? 0 : 1).ThenBy(e => e.Title).ToList();
        }

        public Dictionary<int, List<VerifiedSkillRow>> SelectedRoles(IList<FreelancerProfile> profiles, IDictionary<int, List<VerifiedSkillRow>> evidence)
        {
            var ids = profiles.Select(p => p.UserAccountId).ToArray();
            var secondary = new List<FreelancerSecondaryRole>();
            for (var start = 0; start < ids.Length; start += 500)
            {
                var batch = ids.Skip(start).Take(500).ToArray();
                secondary.AddRange(db.Set<FreelancerSecondaryRole>().AsNoTracking().Where(r => batch.Contains(r.UserAccountId)).ToList());
            }
            var byUser = secondary.ToLookup(r => r.UserAccountId);
            return profiles.ToDictionary(p => p.UserAccountId, p => SelectedRoles(p, byUser[p.UserAccountId], evidence[p.UserAccountId]));
        }

        // Talent/dashboard can pass already loaded evidence to avoid repeated evidence queries.
        public Dictionary<int, string> SelectedRoleLabels(IList<FreelancerProfile> profiles, IDictionary<int, List<VerifiedSkillRow>> evidence = null)
        {
            var roles = SelectedRoles(profiles, evidence ?? Evidence(profiles.Select(p => p.UserAccountId).ToArray()));
            return profiles.ToDictionary(p => p.UserAccountId, p => string.Join(" · ", roles[p.UserAccountId]
                .Select(r => (r.SkillId == p.CoreSkillTrackId ? "Chính: " : "Phụ: ") + r.Title)));
        }

        public static MatchCard Score(Project project, IList<ProjectSkillRequirement> skills, IEnumerable<VerifiedSkillRow> evidence, string role, string industry, int id, string name, string kind, IEnumerable<VerifiedSkillRow> selectedRoles = null)
        {
            var card = new MatchCard { Id = id, Name = name, Kind = kind, Subtitle = role, Required = skills.Count };
            var levels = evidence.GroupBy(e => e.SkillId).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Level).First());
            foreach (var skill in skills)
            {
                VerifiedSkillRow held;
                if (levels.TryGetValue(skill.SkillTrackId, out held) && held.Level >= skill.MinimumLevel)
                {
                    card.Covered++;
                    card.Reasons.Add(held.Title + " · " + LevelName(held.Level) + " · " + held.Evidence);
                }
                else card.Reasons.Add("Còn thiếu: " + (skill.Skill?.Title ?? "Kỹ năng #" + skill.SkillTrackId) + " ở mức " + LevelName(skill.MinimumLevel));
            }
            var needed = Tokens(string.Join(" ", project.Requirements.Select(r => r.Title)));
            var roleMatches = (selectedRoles ?? Enumerable.Empty<VerifiedSkillRow>()).Where(r => levels.ContainsKey(r.SkillId))
                .Select(r => levels[r.SkillId]).GroupBy(r => r.SkillId).Select(g => g.First())
                .Where(r => needed.Count > 0 && Tokens(r.Title).Overlaps(needed)).ToList();
            var roleMatch = roleMatches.Count > 0;
            var sameIndustry = !string.IsNullOrEmpty(project.Industry) && industry == project.Industry;
            card.Score = skills.Count == 0 ? (roleMatch ? 60 : 0) + (sameIndustry && roleMatch ? 10 : 0) :
                (int)Math.Round(70.0 * card.Covered / skills.Count) + (roleMatch && card.Covered > 0 ? 20 : 0) + (sameIndustry && card.Covered > 0 ? 10 : 0);
            if (skills.Count == 0) card.Reasons.Add("Chưa khai báo kỹ năng cho dự án; chỉ đối chiếu vai trò đã chọn và còn minh chứng hợp lệ, chưa đủ cơ sở đánh giá năng lực.");
            if (roleMatch) card.Reasons.Add("Vai trò chính/phụ đã xác minh phù hợp: " + string.Join(", ", roleMatches.Select(r => r.Title)) + (skills.Count > 0 && card.Covered == 0 ? " · chưa cộng điểm vai trò vì chưa đáp ứng kỹ năng yêu cầu." : "."));
            else card.Reasons.Add("Chưa có vai trò chính/phụ đã chọn với minh chứng còn hiệu lực phù hợp; không cộng điểm vai trò. Chức danh tự giới thiệu không được tính điểm.");
            if (sameIndustry) card.Reasons.Add("Cùng nhóm ngành " + industry);
            return card;
        }

        public SkillMatchPage ForProject(Project project)
        {
            var page = new SkillMatchPage { Project = project, Skills = db.ProjectSkillRequirements.Include(s => s.Skill).Where(s => s.ProjectId == project.Id).ToList() };
            var profiles = db.FreelancerProfiles.AsNoTracking().Include(p => p.User).Where(p => p.User.Role == "Freelancer" && !p.User.IsSuspended && p.User.EmailVerifiedAtUtc.HasValue && p.IsPublic != false && p.IsAvailable).OrderByDescending(p => p.UpdatedAtUtc).Take(500).ToList();
            var ids = profiles.Select(p => p.UserAccountId).ToArray();
            var skills = Evidence(ids);
            var roles = SelectedRoles(profiles, skills);
            if (project.ProjectType != "TEAM") page.People = profiles.Where(p => p.MinimumProjectBudget <= project.Budget)
                .Select(p => Score(project, page.Skills, skills[p.UserAccountId], p.CoreRole, p.Industry, p.UserAccountId, p.User.Name, "freelancer", roles[p.UserAccountId]))
                .Where(c => page.Skills.Count > 0 ? c.Covered > 0 : c.Score > 0).OrderByDescending(c => c.Score).ThenBy(c => c.Id).Take(12).ToList();
            if (project.ProjectType != "SOLO")
            {
                var groups = db.FreelancerGroups.AsNoTracking().Include(g => g.Leader).Where(g => g.IsPublic != false && !g.Leader.IsSuspended && (!g.MinimumBudget.HasValue || g.MinimumBudget <= project.Budget)).OrderByDescending(g => g.Id).Take(200).ToList();
                var groupIds = groups.Select(g => g.Id).ToArray();
                var members = db.FreelancerGroupMembers.AsNoTracking().Where(m => groupIds.Contains(m.FreelancerGroupId) && m.Status == "Active" && ids.Contains(m.UserAccountId)).Select(m => new { m.FreelancerGroupId, m.UserAccountId }).ToList();
                foreach (var g in groups.Where(g => ids.Contains(g.LeaderId)))
                {
                    var people = members.Where(m => m.FreelancerGroupId == g.Id).Select(m => profiles.Single(p => p.UserAccountId == m.UserAccountId)).ToList();
                    if (people.Count == 0) continue;
                    var card = Score(project, page.Skills, people.SelectMany(p => skills[p.UserAccountId]), string.Join(", ", people.Select(p => p.CoreRole)), g.Industry ?? people[0].Industry, g.Id, g.Name, "group", people.SelectMany(p => roles[p.UserAccountId]));
                    card.Subtitle = people.Count + " thành viên công khai đang nhận việc · Đại diện: " + g.Leader.Name;
                    card.Reasons.Add("Kỹ năng được hợp nhất từ thành viên; số lượng và phân công cần được xác nhận trong chào giá nhóm.");
                    if (page.Skills.Count > 0 ? card.Covered > 0 : card.Score > 0) page.Groups.Add(card);
                }
                page.Groups = page.Groups.OrderByDescending(c => c.Score).ThenBy(c => c.Id).Take(12).ToList();
            }
            page.Positions = new PositionMatchingService(db).ForProject(project);
            return page;
        }

        public List<MatchCard> ForFreelancer(int userId)
        {
            var profile = db.FreelancerProfiles.AsNoTracking().SingleOrDefault(p => p.UserAccountId == userId);
            if (profile == null) return new List<MatchCard>();
            var today = DateTime.UtcNow.Date;
            var leadingGroups = db.FreelancerGroups.AsNoTracking().Where(g => g.LeaderId == userId && db.FreelancerGroupMembers.Any(m => m.FreelancerGroupId == g.Id && m.UserAccountId == userId && m.Status == "Active")).OrderByDescending(g => g.Id).Take(40).ToList();
            var leadingIds = leadingGroups.Select(g => g.Id).ToArray();
            var groupMembers = db.FreelancerGroupMembers.AsNoTracking().Where(m => leadingIds.Contains(m.FreelancerGroupId) && m.Status == "Active" && !m.User.IsSuspended && m.User.Role == "Freelancer").Select(m => new { m.FreelancerGroupId, m.UserAccountId }).ToList();
            var memberIds = groupMembers.Select(m => m.UserAccountId).Concat(new[] { userId }).Distinct().ToArray();
            var evidence = Evidence(memberIds);
            var memberProfiles = db.FreelancerProfiles.AsNoTracking().Where(p => memberIds.Contains(p.UserAccountId)).ToList();
            var roles = SelectedRoles(memberProfiles, evidence);
            var canLead = leadingIds.Length > 0;
            var projects = db.Projects.AsNoTracking().Include(p => p.Requirements).Where(p => p.Status == "Published" && (p.ProjectType != "TEAM" || canLead) && !p.Client.IsSuspended && p.Budget >= profile.MinimumProjectBudget && (!p.DeadlineUtc.HasValue || p.DeadlineUtc >= today) && p.Requirements.Any(r => db.RoleAssignments.Count(a => a.RoleRequirementId == r.Id) < r.RequiredCount)).OrderByDescending(p => p.Id).Take(200).ToList();
            var ids = projects.Select(p => p.Id).ToArray();
            var needed = db.ProjectSkillRequirements.AsNoTracking().Include(s => s.Skill).Where(s => ids.Contains(s.ProjectId)).ToList();
            var held = evidence[userId];
            var applied = new HashSet<int>(db.Proposals.Where(a => ids.Contains(a.ProjectId) && a.ApplicantId == userId && a.Status != "Withdrawn" && a.Status != "Rejected").Select(a => a.ProjectId));
            return projects.Where(p => !applied.Contains(p.Id))
                .Select(p => {
                    var requirements = needed.Where(s => s.ProjectId == p.Id).ToList();
                    var options = new List<MatchCard>();
                    if (p.ProjectType != "TEAM") options.Add(Score(p, requirements, held, profile.CoreRole, profile.Industry, p.Id, p.Title, "project", roles[userId]));
                    if (p.ProjectType != "SOLO") foreach (var group in leadingGroups.Where(g => !g.MinimumBudget.HasValue || g.MinimumBudget <= p.Budget))
                    {
                        var roster = groupMembers.Where(m => m.FreelancerGroupId == group.Id).Select(m => m.UserAccountId).ToArray();
                        var groupRoles = roster.Where(id => roles.ContainsKey(id)).SelectMany(id => roles[id]).ToList();
                        var card = Score(p, requirements, roster.SelectMany(id => evidence[id]), string.Join(", ", groupRoles.Select(r => r.Title)), group.Industry ?? profile.Industry, p.Id, p.Title, "project", groupRoles);
                        card.Subtitle = "Ứng tuyển theo nhóm " + group.Name + " · bạn là đại diện";
                        options.Add(card);
                    }
                    return options.OrderByDescending(c => c.Score).FirstOrDefault();
                })
                .Where(c => c != null && (c.Required > 0 ? c.Covered > 0 : c.Score > 0)).OrderByDescending(c => c.Score).ThenByDescending(c => c.Id).Take(8).ToList();
        }
    }
}
