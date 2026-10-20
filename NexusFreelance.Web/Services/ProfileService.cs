using System;
using System.Collections.Generic;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Services
{
    public sealed class ProfileService
    {
        private readonly AppDbContext db;
        public ProfileService(AppDbContext db) { this.db = db; }
        public System.Collections.Generic.List<ProjectChoice> CompletedProjects(int freelancerId)
        {
            return db.Projects.Where(p => p.Status == "Completed" && db.RoleAssignments.Any(a => a.ProjectId == p.Id && a.FreelancerId == freelancerId))
                .OrderBy(p => p.Title).Select(p => new ProjectChoice { Id = p.Id, Title = p.Title }).ToList();
        }

        public FreelancerProfilePage Load(int freelancerId)
        {
            var profile = db.FreelancerProfiles.SingleOrDefault(p => p.UserAccountId == freelancerId);
            var verified = new SmartMatchingService(db).Evidence(new[] { freelancerId })[freelancerId];
            var secondaryIds = db.Set<FreelancerSecondaryRole>().Where(r => r.UserAccountId == freelancerId).Select(r => r.SkillTrackId).ToList();
            var selectedIds = secondaryIds.Concat(profile?.CoreSkillTrackId == null ? new int[0] : new[] { profile.CoreSkillTrackId.Value });
            return new FreelancerProfilePage
            {
                VerifiedSkills = verified,
                HasInactiveRoleSelections = selectedIds.Any(id => !verified.Any(s => s.SkillId == id)),
                Profile = new FreelancerProfileForm
                {
                    CoreSkillTrackId = profile?.CoreSkillTrackId, CoreRole = profile?.CoreRole ?? "",
                    SecondarySkillTrackIds = secondaryIds,
                    Bio = profile?.Bio ?? "", Industry = profile?.Industry, Location = profile?.Location, YearsExperience = profile?.YearsExperience ?? 0, MinimumProjectBudget = profile?.MinimumProjectBudget ?? 0,
                    IsAvailable = profile != null && profile.IsAvailable,
                    IsPublic = profile == null || profile.IsPublic != false
                },
                CompletedProjects = CompletedProjects(freelancerId),
                Portfolio = db.PortfolioItems.Where(p => p.FreelancerId == freelancerId)
                    .OrderByDescending(p => p.CreatedAtUtc).ToList()
            };
        }

        public static void ValidateRoles(FreelancerProfileForm form, IEnumerable<VerifiedSkillRow> evidence)
        {
            var secondary = form.SecondarySkillTrackIds ?? new List<int>();
            if (secondary.Count != secondary.Distinct().Count())
                throw new InvalidOperationException("Vai trò phụ không được trùng nhau.");
            if (secondary.Count > 0 && !form.CoreSkillTrackId.HasValue)
                throw new InvalidOperationException("Vui lòng chọn vai trò chính trước khi chọn vai trò phụ.");
            if (form.CoreSkillTrackId.HasValue && secondary.Contains(form.CoreSkillTrackId.Value))
                throw new InvalidOperationException("Vai trò phụ không được trùng với vai trò chính.");
            var validIds = new HashSet<int>(evidence.Select(s => s.SkillId));
            if ((form.CoreSkillTrackId.HasValue && !validIds.Contains(form.CoreSkillTrackId.Value)) || secondary.Any(id => !validIds.Contains(id)))
                throw new InvalidOperationException("Vai trò chính và vai trò phụ phải có chứng chỉ hoặc minh chứng đã xác minh còn hiệu lực. Vui lòng chọn lại.");
        }

        public void SaveProfile(int freelancerId, FreelancerProfileForm form)
        {
            if (form == null) throw new InvalidOperationException("Dữ liệu hồ sơ không hợp lệ.");
            var role = (form.CoreRole ?? "").Trim();
            if (role.Length == 0 || role.Length > 100) throw new InvalidOperationException("Chức danh nghề nghiệp phải từ 1 đến 100 ký tự.");
            if (!IndustryCatalog.IsValid(form.Industry)) throw new InvalidOperationException("Nhóm ngành không hợp lệ.");
            ValidateRoles(form, new SmartMatchingService(db).Evidence(new[] { freelancerId })[freelancerId]);
            var secondaryIds = form.SecondarySkillTrackIds ?? new List<int>();
            var secondaryRoles = db.Set<FreelancerSecondaryRole>().Where(r => r.UserAccountId == freelancerId).ToList();
            var existing = db.FreelancerProfiles.SingleOrDefault(p => p.UserAccountId == freelancerId);
            if (existing == null)
            {
                existing = new FreelancerProfile { UserAccountId = freelancerId };
                db.FreelancerProfiles.Add(existing);
            }
            existing.Industry = IndustryCatalog.Normalize(form.Industry); existing.Location = (form.Location ?? "").Trim();
            existing.YearsExperience = form.YearsExperience; existing.MinimumProjectBudget = form.MinimumProjectBudget;
            existing.CoreSkillTrackId = form.CoreSkillTrackId;
            existing.CoreRole = role;
            existing.Bio = (form.Bio ?? "").Trim();
            existing.IsAvailable = form.IsAvailable;
            existing.IsPublic = form.IsPublic;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            db.Set<FreelancerSecondaryRole>().RemoveRange(secondaryRoles.Where(r => !secondaryIds.Contains(r.SkillTrackId)));
            foreach (var skillId in secondaryIds.Where(id => !secondaryRoles.Any(r => r.SkillTrackId == id)))
                db.Set<FreelancerSecondaryRole>().Add(new FreelancerSecondaryRole { UserAccountId = freelancerId, SkillTrackId = skillId });
            db.SaveChanges();
        }

        public void AddPortfolio(int freelancerId, PortfolioItemForm form)
        {
            db.PortfolioItems.Add(new PortfolioItem
            {
                FreelancerId = freelancerId, Role = form.Role, Skills = form.Skills, Outcome = form.Outcome, ProjectId = form.ProjectId,
                Title = form.Title.Trim(), Summary = form.Summary.Trim(), Url = form.Url.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        public PortfolioItem FindOwnedPortfolio(int itemId, int freelancerId)
        {
            return db.PortfolioItems.SingleOrDefault(p => p.Id == itemId && p.FreelancerId == freelancerId);
        }

        public void UpdatePortfolio(PortfolioItem item, PortfolioItemForm form)
        {
            item.Role = form.Role; item.Skills = form.Skills; item.Outcome = form.Outcome; item.ProjectId = form.ProjectId;
            item.Title = form.Title.Trim();
            item.Summary = form.Summary.Trim();
            item.Url = form.Url.Trim();
            db.SaveChanges();
        }

        public void DeletePortfolio(PortfolioItem item)
        {
            foreach(var evidence in db.SkillEvidence.Where(e=>e.PortfolioItemId==item.Id)) evidence.PortfolioItemId=null;
            db.PortfolioItems.Remove(item);
            db.SaveChanges();
        }
    }
}
