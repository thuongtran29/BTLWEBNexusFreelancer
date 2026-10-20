using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Services
{
    public sealed class ProjectEditResult
    {
        public bool Changed { get; set; }
        public int Recipients { get; set; }
    }

    public sealed class ProjectEditingService
    {
        private readonly AppDbContext db;
        public ProjectEditingService(AppDbContext database) { db = database; }
        // Both entry points validate ProjectForm annotations and ProjectInputPolicy first.
        public Project Create(int actorId, ProjectForm form)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                var project = new Project { ClientId = actorId, Title = form.Title.Trim(), Description = form.Description.Trim(),
                    Industry = IndustryCatalog.Normalize(form.Industry), Budget = form.Budget,
                    DeadlineUtc = form.Deadline.HasValue ? (DateTime?)DateTime.SpecifyKind(form.Deadline.Value.Date, DateTimeKind.Utc) : null,
                    ProjectType = form.ProjectType, Status = "Draft", CreatedAtUtc = DateTime.UtcNow };
                db.Projects.Add(project);
                foreach (var skillId in form.SkillIds.Distinct()) db.ProjectSkillRequirements.Add(new ProjectSkillRequirement {
                    Project = project, SkillTrackId = skillId, MinimumLevel = form.MinimumSkillLevel });
                db.SaveChanges(); tx.Commit(); return project;
            }
        }
        public static bool CanEdit(Project project) => project != null &&
            new[] { "Draft", "Published", "Paused", "AwaitingEscrow", "InProgress" }.Contains(project.Status);
        public bool IsEditable(Project project) => CanEdit(project) && !db.EscrowDisputes.Any(d => d.ProjectId == project.Id && d.Status == "Pending");
        public bool InformationOnly(Project project) => project.Status == "AwaitingEscrow" || project.Status == "InProgress" ||
            db.Contracts.Any(c => c.ProjectId == project.Id);
        public string Version(Project project)
        {
            var skills = db.ProjectSkillRequirements.Where(s => s.ProjectId == project.Id).OrderBy(s => s.SkillTrackId)
                .Select(s => new { s.SkillTrackId, s.MinimumLevel }).ToList();
            return ContractService.Digest(new JavaScriptSerializer().Serialize(new {
                project.Status, project.Title, project.Description, project.Industry, project.ProjectType,
                Budget = project.Budget.ToString("0.##", CultureInfo.InvariantCulture),
                Deadline = project.DeadlineUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                InformationOnly = InformationOnly(project), Skills = skills
            }));
        }

        public ProjectEditResult Save(int projectId, int actorId, ProjectForm form)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var id = db.Database.SqlQuery<int>("SELECT Id FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", projectId).SingleOrDefault();
                if (id == 0) throw new InvalidOperationException("Dự án không còn tồn tại.");
                var project = db.Projects.Single(p => p.Id == id); db.Entry(project).Reload();
                if (project.ClientId != actorId || !IsEditable(project)) throw new InvalidOperationException("Dự án đang khóa chỉnh sửa do giai đoạn hiện tại hoặc đang có tranh chấp.");
                if ((project.Status != "Draft" || !string.IsNullOrEmpty(form.EditVersion)) && form.EditVersion != Version(project))
                    throw new InvalidOperationException("Dự án đã thay đổi trong lúc bạn chỉnh sửa. Mở lại bản mới nhất, đối chiếu nội dung rồi lưu lại.");
                var previous = db.ProjectSkillRequirements.Where(s => s.ProjectId == id).ToList();
                var ids = (form.SkillIds ?? new int[0]).Distinct().OrderBy(s => s).ToArray();
                var informationOnly = InformationOnly(project);
                var skillChanged = !ids.SequenceEqual(previous.Select(s => s.SkillTrackId).OrderBy(s => s)) ||
                    (informationOnly ? form.MinimumSkillLevel != (previous.Select(s => (int?)s.MinimumLevel).Min() ?? 1) :
                        previous.Any(s => s.MinimumLevel != form.MinimumSkillLevel));
                var deadline = form.Deadline.HasValue ? (DateTime?)DateTime.SpecifyKind(form.Deadline.Value.Date, DateTimeKind.Utc) : null;
                var industry = IndustryCatalog.Normalize(form.Industry);
                if (project.Status != "Draft" && form.ProjectType != project.ProjectType)
                    throw new InvalidOperationException("Loại dự án đã công bố được giữ nguyên để bảo toàn các đề xuất đã gửi.");
                if (informationOnly && (project.Budget != form.Budget || project.ProjectType != form.ProjectType ||
                    project.Industry != industry || project.DeadlineUtc != deadline || skillChanged))
                    throw new InvalidOperationException("Dự án đã có hợp đồng: chỉ cập nhật tên và mô tả chung. Điều khoản, ngân sách, kỹ năng và mốc đã chốt được giữ nguyên.");
                var changed = new List<string>();
                if (project.Title != form.Title.Trim()) changed.Add("tên dự án");
                if (project.Description != form.Description.Trim()) changed.Add("mô tả");
                if (project.Industry != industry) changed.Add("ngành nghề");
                if (project.Budget != form.Budget) changed.Add("ngân sách");
                if (project.DeadlineUtc != deadline) changed.Add("hạn dự kiến");
                if (project.ProjectType != form.ProjectType) changed.Add("loại dự án");
                if (skillChanged) changed.Add("kỹ năng yêu cầu");
                var result = new ProjectEditResult { Changed = changed.Count > 0 };
                if (!result.Changed) { tx.Commit(); return result; }
                if (!informationOnly)
                {
                    db.ProjectSkillRequirements.RemoveRange(previous.Where(s => !ids.Contains(s.SkillTrackId)));
                    foreach (var old in previous.Where(s => ids.Contains(s.SkillTrackId))) old.MinimumLevel = form.MinimumSkillLevel;
                    foreach (var skillId in ids.Except(previous.Select(s => s.SkillTrackId)))
                        db.ProjectSkillRequirements.Add(new ProjectSkillRequirement { ProjectId = id, SkillTrackId = skillId, MinimumLevel = form.MinimumSkillLevel });
                    project.Industry = industry; project.Budget = form.Budget; project.ProjectType = form.ProjectType; project.DeadlineUtc = deadline;
                }
                project.Title = form.Title.Trim(); project.Description = form.Description.Trim();
                db.ActivityLogs.Add(new ActivityLog { ProjectId = id, ActorId = actorId,
                    Action = "Updated project information: " + string.Join(", ", changed), CreatedAtUtc = DateTime.UtcNow });
                if (project.Status != "Draft")
                {
                    result.Recipients = NotifyParticipants(project, actorId, changed);
                }
                db.SaveChanges(); tx.Commit(); return result;
            }
        }

        // Caller holds the project's mutation lock; notifications commit with that same change.
        public int NotifyParticipants(Project project, int actorId, IEnumerable<string> changed)
        {
            if (project.ClientId != actorId) throw new InvalidOperationException("Chỉ chủ dự án được gửi cập nhật.");
            if (project.Status == "Draft") return 0;
            var id = project.Id;
            var recipients = db.Proposals.Where(p => p.ProjectId == id &&
                (p.Status == "Pending" || p.Status == "Shortlisted")).Select(p => p.ApplicantId)
                .Union(db.ContractMembers.Where(m => m.Contract.ProjectId == id && m.Contract.Status != "Declined" && m.Contract.Status != "Terminated").Select(m => m.UserAccountId))
                .Union(db.TeamMembers.Where(m => m.Team.ProjectId == id && db.Workspaces.Any(w => w.ProjectId == id && w.ProjectTeamId == m.ProjectTeamId && !w.ContractId.HasValue)).Select(m => m.UserAccountId))
                .Where(userId => userId != actorId).Distinct().ToList();
            var title = "Dự án vừa cập nhật: " + project.Title.Substring(0, Math.Min(75, project.Title.Length)) + " · " + string.Join(", ", changed);
            title = title.Substring(0, Math.Min(200, title.Length));
            foreach (var recipient in recipients) NotificationService.Queue(db, recipient, title, "/Projects/Details/" + id, actorId, id);
            return recipients.Count;
        }
    }
}
