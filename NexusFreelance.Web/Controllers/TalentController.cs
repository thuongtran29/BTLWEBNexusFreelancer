using System;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Controllers
{
    // Public directory. Never project email, group invitations, private bids or activity events.
    public class TalentController : BaseController
    {
        private const int PageSize = 12;

        [HttpGet]
        public ActionResult Index(string q = "", string kind = "freelancer", int page = 1, string industry = "", int? skillId = null, bool verifiedOnly = false, string sort = "recent")
        {
            q = (q ?? "").Trim();
            if (q.Length > 100) return new HttpStatusCodeResult(400);
            if (!Services.IndustryCatalog.IsValid(industry) || (skillId.HasValue && skillId.Value<1) || !new[]{"recent","az","za"}.Contains(sort) || !ModelState.IsValid) return new HttpStatusCodeResult(400);
            ViewBag.Industry=industry; ViewBag.SkillId=skillId;ViewBag.VerifiedOnly=verifiedOnly;ViewBag.Sort=sort;
            ViewBag.TrackOptions=Db.SkillTracks.Where(t=>t.IsActive!=false).OrderBy(t=>t.Title).Take(200).ToList();
            var today=DateTime.Today;
            var selectedSkillId=skillId.GetValueOrDefault();
            var skilledUsers = Db.SkillClaims.Where(c => !c.IsDeleted && (selectedSkillId == 0 || c.SkillTrackId == selectedSkillId) && (!verifiedOnly || Db.SkillEvidenceRequests.Any(r => r.SkillClaimId == c.Id && r.Status == "Approved" && (!r.ValidUntil.HasValue || r.ValidUntil >= today)))).Select(c => c.UserAccountId)
                .Union(Db.EarnedSkillBadges.Where(b => b.RevokedAtUtc == null && b.SkillTrack.IsActive != false && (selectedSkillId == 0 || b.SkillTrackId == selectedSkillId)).Select(b => b.UserAccountId))
                .Union(Db.ExternalCertifications.Where(c => c.Status == "Verified" && c.SkillTrackId.HasValue && c.Skill.IsActive != false && (selectedSkillId == 0 || c.SkillTrackId == selectedSkillId) && (!c.ExpiresOn.HasValue || c.ExpiresOn >= today)).Select(c => c.UserAccountId));
            kind = string.Equals(kind, "group", StringComparison.OrdinalIgnoreCase) ? "group" : "freelancer";
            var result = new PublicTalentDirectoryPage { Query = q, Kind = kind };

            if (kind == "group")
            {
                var groups = Db.FreelancerGroups.Where(g => !g.Leader.IsSuspended && g.Leader.Role == "Freelancer" &&
                    (g.IsPublic == null || g.IsPublic == true) &&
                    !Db.FreelancerProfiles.Any(p => p.UserAccountId == g.LeaderId && p.IsPublic == false));
                if (q.Length > 0) groups = groups.Where(g => g.Name.Contains(q) || g.Description.Contains(q));
                if (!string.IsNullOrEmpty(industry)) groups=groups.Where(g=>Db.FreelancerGroupMembers.Any(m=>m.FreelancerGroupId==g.Id && m.Status=="Active" && !m.User.IsSuspended && m.User.Role=="Freelancer" && Db.FreelancerProfiles.Any(p=>p.UserAccountId==m.UserAccountId && p.IsPublic!=false && p.Industry==industry)));
                if(skillId.HasValue || verifiedOnly) groups=groups.Where(g=>Db.FreelancerGroupMembers.Any(m=>m.FreelancerGroupId==g.Id && m.Status=="Active" && !m.User.IsSuspended && m.User.Role=="Freelancer" && !Db.FreelancerProfiles.Any(p=>p.UserAccountId==m.UserAccountId && p.IsPublic==false) && skilledUsers.Contains(m.UserAccountId)));
                result.Total = groups.Count();
                result.Page = Math.Min(Math.Max(1, page), Math.Max(1, (int)((result.Total + PageSize - 1L) / PageSize)));
                var orderedGroups=sort=="az"?groups.OrderBy(g=>g.Name):sort=="za"?groups.OrderByDescending(g=>g.Name):groups.OrderByDescending(g=>g.Id);
                result.Groups = orderedGroups.ThenBy(g=>g.Id).Skip((result.Page - 1) * PageSize).Take(PageSize)
                    .Select(g => new PublicGroupCard
                    {
                        Id = g.Id, Name = g.Name, Description = g.Description, LeaderName = g.Leader.Name,
                        MemberCount = Db.FreelancerGroupMembers.Count(m => m.FreelancerGroupId == g.Id &&
                            m.Status == "Active" && !m.User.IsSuspended && m.User.Role == "Freelancer" &&
                            Db.FreelancerProfiles.Any(p => p.UserAccountId == m.UserAccountId && p.IsPublic != false))
                    }).ToList();
            }
            else
            {
                var freelancers = Db.FreelancerProfiles.Where(p => !p.User.IsSuspended && p.User.Role == "Freelancer" &&
                    (p.IsPublic == null || p.IsPublic == true));
                if (q.Length > 0) freelancers = freelancers.Where(p => p.User.Name.Contains(q) ||
                    p.CoreRole.Contains(q) || p.Bio.Contains(q));
                if(!string.IsNullOrEmpty(industry)) freelancers=freelancers.Where(p=>p.Industry==industry);
                if(skillId.HasValue || verifiedOnly) freelancers=freelancers.Where(p=>skilledUsers.Contains(p.UserAccountId));
                result.Total = freelancers.Count();
                result.Page = Math.Min(Math.Max(1, page), Math.Max(1, (int)((result.Total + PageSize - 1L) / PageSize)));
                var orderedPeople=sort=="az"?freelancers.OrderBy(p=>p.User.Name):sort=="za"?freelancers.OrderByDescending(p=>p.User.Name):freelancers.OrderByDescending(p=>p.UpdatedAtUtc);
                result.Freelancers = orderedPeople.ThenByDescending(p => p.UserAccountId)
                    .Skip((result.Page - 1) * PageSize).Take(PageSize)
                    .Select(p => new PublicFreelancerCard
                    {
                        Id = p.UserAccountId, Name = p.User.Name, CoreRole = p.CoreRole,
                        Bio = p.Bio, IsAvailable = p.IsAvailable
                    }).ToList();
            }
            var personIds = result.Freelancers.Select(p => p.Id).ToArray();
            ViewBag.SelectedRoles = new Services.SmartMatchingService(Db).SelectedRoleLabels(Db.FreelancerProfiles.AsNoTracking().Where(p => personIds.Contains(p.UserAccountId)).ToList());
            return View(result);
        }

        [HttpGet]
        public ActionResult Freelancer(int id)
        {
            var profile = Db.FreelancerProfiles.Where(p => p.UserAccountId == id &&
                    !p.User.IsSuspended && p.User.Role == "Freelancer" &&
                    (p.IsPublic == null || p.IsPublic == true))
                .Select(p => new PublicFreelancerCard
                {
                    Id = p.UserAccountId, Name = p.User.Name, CoreRole = p.CoreRole,
                    Bio = p.Bio, IsAvailable = p.IsAvailable
                }).SingleOrDefault();
            if (profile == null) return HttpNotFound();
            var profileDetails = Db.FreelancerProfiles.AsNoTracking().Single(p => p.UserAccountId == id);
            ViewBag.ProfileDetails = profileDetails;
            var matching = new Services.SmartMatchingService(Db); var evidence = matching.Evidence(new[] { id });
            ViewBag.CoreSkills = evidence[id];
            ViewBag.SelectedRoles = matching.SelectedRoleLabels(new[] { profileDetails }, evidence)[id];
            // A verified certificate remains valid for its entire expiry calendar day.
            var today = DateTime.Today;
            ViewBag.VerifiedClaims = Db.SkillClaims.Include(c=>c.Skill).Include(c=>c.User).Where(c=>c.UserAccountId==id && !c.IsDeleted && Db.SkillEvidenceRequests.Any(r=>r.SkillClaimId==c.Id && r.Status=="Approved" && (!r.ValidUntil.HasValue || r.ValidUntil>=today))).ToList();
            var page = new PublicFreelancerPage { Freelancer = profile };
            page.Portfolio = Db.PortfolioItems.Where(p => p.FreelancerId == id)
                .OrderByDescending(p => p.CreatedAtUtc).Take(30).ToList()
                .Where(p => IsSafePortfolioLink(p.Url)).ToList();
            page.Badges = Db.EarnedSkillBadges.Include(b => b.SkillTrack).Include(b => b.EvidenceAttempt)
                .Where(b => b.UserAccountId == id && b.RevokedAtUtc == null)
                .OrderByDescending(b => b.IssuedAtUtc).Take(30).ToList();
            page.Certifications = Db.ExternalCertifications.Where(c => c.UserAccountId == id &&
                    c.Status == "Verified" && (c.ExpiresOn == null || c.ExpiresOn >= today))
                .OrderByDescending(c => c.IssuedOn).Take(30).ToList();
            return View(page);
        }

        [HttpGet]
        public ActionResult Group(int id)
        {
            var group = Db.FreelancerGroups.Where(g => g.Id == id &&
                    !g.Leader.IsSuspended && g.Leader.Role == "Freelancer" &&
                    (g.IsPublic == null || g.IsPublic == true) &&
                    !Db.FreelancerProfiles.Any(p => p.UserAccountId == g.LeaderId && p.IsPublic == false))
                .Select(g => new PublicGroupCard { Id = g.Id, Name = g.Name,
                    Description = g.Description, LeaderName = g.Leader.Name }).SingleOrDefault();
            if (group == null) return HttpNotFound();
            var details = Db.FreelancerGroups.AsNoTracking().Single(g => g.Id == id);
            var members = Db.FreelancerGroupMembers.Where(m => m.FreelancerGroupId == id &&
                    m.Status == "Active" && !m.User.IsSuspended && m.User.Role == "Freelancer" &&
                    Db.FreelancerProfiles.Any(p => p.UserAccountId == m.UserAccountId && p.IsPublic != false))
                .OrderBy(m => m.User.Name)
                .Select(m => new PublicFreelancerCard { Id = m.UserAccountId, Name = m.User.Name }).ToList();
            var memberIds = members.Select(m => m.Id).ToArray();
            var roles = Db.FreelancerProfiles.Where(p => memberIds.Contains(p.UserAccountId))
                .Select(p => new { p.UserAccountId, p.CoreRole, p.IsAvailable }).ToList()
                .ToDictionary(p => p.UserAccountId);
            foreach (var member in members)
            {
                if (roles.ContainsKey(member.Id)) {
                    member.CoreRole = roles[member.Id].CoreRole;
                    member.IsAvailable = roles[member.Id].IsAvailable;
                }
            }
            var matching = new Services.SmartMatchingService(Db);
            var evidence = matching.Evidence(memberIds);
            ViewBag.GroupCoreSkills = evidence.Values.SelectMany(v => v).GroupBy(v => v.SkillId).Select(g => g.OrderByDescending(v => v.Level).First()).ToList();
            ViewBag.SelectedRoles = matching.SelectedRoleLabels(Db.FreelancerProfiles.AsNoTracking().Where(p => memberIds.Contains(p.UserAccountId)).ToList(), evidence);
            group.MemberCount = members.Count;
            return View(new PublicGroupPage {
                Group = group, Members = members, LeaderId = details.LeaderId,
                HasLeaderProfile = members.Any(m => m.Id == details.LeaderId),
                Industry = details.Industry, WorkStyle = details.WorkStyle, Specialties = details.Specialties,
                Collaboration = details.Collaboration, MinimumBudget = details.MinimumBudget, CreatedAtUtc = details.CreatedAtUtc,
                Portfolio = Db.PortfolioItems.Where(p => memberIds.Contains(p.FreelancerId))
                    .OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id).Take(12)
                    .Select(p => new PublicGroupPortfolioItem { FreelancerId = p.FreelancerId, FreelancerName = p.Freelancer.Name,
                        Title = p.Title, Summary = p.Summary, Role = p.Role, Url = p.Url }).ToList()
                    .Where(p => IsSafePortfolioLink(p.Url)).ToList()
            });
        }

        private static bool IsSafePortfolioLink(string url)
        {
            Uri parsed;
            return Uri.TryCreate(url, UriKind.Absolute, out parsed) && parsed.Scheme == Uri.UriSchemeHttps;
        }
    }
}
