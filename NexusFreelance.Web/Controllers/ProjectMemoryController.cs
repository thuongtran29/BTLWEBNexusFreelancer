using System;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public class ProjectMemoryController : BaseController
    {
        private bool CanRead(Project project, int userId)
        {
            return project.ClientId == userId || (!Db.Contracts.Any(c=>c.ProjectId==project.Id) && Db.TeamMembers.Any(m => m.Team.ProjectId == project.Id && m.UserAccountId == userId));
        }

        [HttpGet]
        public ActionResult Index(int projectId, int? version)
        {
            var project = Db.Projects.SingleOrDefault(p => p.Id == projectId);
            if (project == null) return HttpNotFound();
            var user = CurrentUser;
            if (user == null || !CanRead(project, user.Id)) return Forbidden();
            if (project.Status != "Completed") return new HttpStatusCodeResult(409, "Lưu trữ tri thức chỉ mở sau khi dự án hoàn thành.");
            if (version.HasValue && version.Value < 1) return new HttpStatusCodeResult(400);

            var history = Db.ProjectMemories.Where(m => m.ProjectId == projectId).OrderByDescending(m => m.Version).ToList();
            var selected = version.HasValue ? history.SingleOrDefault(m => m.Version == version.Value) : history.FirstOrDefault();
            if (version.HasValue && selected == null) return HttpNotFound();
            ViewBag.Project = project;
            ViewBag.History = history;
            ViewBag.IsClient = project.ClientId == user.Id;
            ViewBag.IsLatest = selected != null && history[0].Id == selected.Id;
            return View(selected);
        }

        // Explicit client action, never run automatically on completion.
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Generate(int projectId)
        {
            var user = CurrentUser;
            var project = Db.Projects.SingleOrDefault(p => p.Id == projectId);
            if (project == null) return HttpNotFound();
            if (user == null || project.ClientId != user.Id) return Forbidden();
            if (project.Status != "Completed") return new HttpStatusCodeResult(409);

            using (var tx = Db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                Db.Database.SqlQuery<int>("SELECT [Id] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", projectId).Single();
                if (Db.ProjectMemories.Any(m => m.ProjectId == projectId)) return new HttpStatusCodeResult(409, "Đã có bản lưu trữ. Hãy chỉnh sửa phiên bản hiện tại.");
                var roles = Db.RoleRequirements.Where(r => r.ProjectId == projectId).Select(r => r.Title).ToList();
                var deliverables = Db.Deliverables.Where(d => d.Workspace.ProjectId == projectId && d.Status == "Approved" &&
                    !Db.Deliverables.Any(next => next.RevisesId == d.Id))
                    .Select(d => d.Title).Take(10).ToList();
                var summary = "Dự án " + project.Title + " đã hoàn thành. Phạm vi và mục tiêu gốc: " + project.Description;
                if (summary.Length > 2000) summary = summary.Substring(0, 2000);
                var memory = new ProjectMemory
                {
                    ProjectId = projectId, EditorId = user.Id, Version = 1, Source = "RuleBasedDemo-v1",
                    ExecutiveSummary = summary,
                    TechStack = "Vai trò tham gia: " + string.Join(", ", roles) + ". Công nghệ thực tế cần Client xác nhận.",
                    ChallengesAndSolutions = "Chưa có thông tin xác thực. Client bổ sung thách thức và giải pháp đã áp dụng.",
                    LessonsLearned = "Client và đội dự án bổ sung bài học thực tế sau khi nghiệm thu.",
                    ReusableAssets = deliverables.Count == 0 ? "Chưa có tài sản được ghi nhận." :
                        "Tên bàn giao đã duyệt (không công khai tệp): " + string.Join("; ", deliverables),
                    PortfolioDraft = "Bản nháp riêng tư: " + project.Title + ". Chỉ chia sẻ sau khi Client cho phép.",
                    CreatedAtUtc = DateTime.UtcNow
                };
                memory.TechStack = memory.TechStack.Substring(0, Math.Min(600, memory.TechStack.Length));
                memory.ReusableAssets = memory.ReusableAssets.Substring(0, Math.Min(1000, memory.ReusableAssets.Length));
                memory.PortfolioDraft = memory.PortfolioDraft.Substring(0, Math.Min(1500, memory.PortfolioDraft.Length));
                Db.ProjectMemories.Add(memory);
                Db.ActivityLogs.Add(new ActivityLog { ProjectId = projectId, ActorId = user.Id,
                    Action = "Generated private project memory draft #1", CreatedAtUtc = DateTime.UtcNow });
                Db.SaveChanges();
                tx.Commit();
            }
            return RedirectToAction("Index", new { projectId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Save(ProjectMemoryForm form)
        {
            if (form == null) return new HttpStatusCodeResult(400);
            var user = CurrentUser;
            var project = Db.Projects.Find(form.ProjectId);
            if (project == null) return HttpNotFound();
            if (user == null || project.ClientId != user.Id) return Forbidden();
            if (project.Status != "Completed") return new HttpStatusCodeResult(409);
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(form.ExecutiveSummary))
            {
                TempData["Error"] = "Nội dung vượt giới hạn hoặc thiếu phần tóm tắt.";
                return RedirectToAction("Index", new { projectId = form.ProjectId });
            }
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                Db.Database.SqlQuery<int>("SELECT [Id] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", form.ProjectId).Single();
                var latest = Db.ProjectMemories.Where(m => m.ProjectId == form.ProjectId)
                    .OrderByDescending(m => m.Version).FirstOrDefault();
                if (latest == null || latest.Version != form.ExpectedVersion)
                {
                    TempData["Error"] = "Tài liệu đã thay đổi. Hãy xem bản mới nhất trước khi lưu.";
                    return RedirectToAction("Index", new { projectId = form.ProjectId });
                }
                Db.ProjectMemories.Add(new ProjectMemory
                {
                    ProjectId = form.ProjectId, EditorId = user.Id, Version = latest.Version + 1, Source = "Manual-v1",
                    ExecutiveSummary = form.ExecutiveSummary.Trim(), TechStack = (form.TechStack ?? "").Trim(),
                    ChallengesAndSolutions = (form.ChallengesAndSolutions ?? "").Trim(),
                    LessonsLearned = (form.LessonsLearned ?? "").Trim(), ReusableAssets = (form.ReusableAssets ?? "").Trim(),
                    PortfolioDraft = (form.PortfolioDraft ?? "").Trim(), CreatedAtUtc = DateTime.UtcNow
                });
                Db.ActivityLogs.Add(new ActivityLog { ProjectId = form.ProjectId, ActorId = user.Id,
                    Action = "Updated private project memory to version #" + (latest.Version + 1), CreatedAtUtc = DateTime.UtcNow });
                Db.SaveChanges();
                tx.Commit();
            }
            TempData["Notice"] = "Đã lưu phiên bản mới. Dữ liệu vẫn ở chế độ riêng tư.";
            return RedirectToAction("Index", new { projectId = form.ProjectId });
        }
    }
}
