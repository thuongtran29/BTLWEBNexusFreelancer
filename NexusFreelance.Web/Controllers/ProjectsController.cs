using System;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Controllers
{
    public class ProjectsController : BaseController
    {
        [HttpGet]
        public ActionResult Details(int id)
        {
            var project = Db.Projects.Include(p => p.Client).Include(p => p.Requirements).SingleOrDefault(p => p.Id == id);
            if (project == null) return HttpNotFound();
            var user = CurrentUser;
            var isOwnerOrAdmin = user != null && (project.ClientId == user.Id || user.Role == "Admin");
            if (project.Status == "Draft" && !isOwnerOrAdmin) return HttpNotFound();
            if ((project.Status == "Paused" || project.Status == "Cancelled") && !isOwnerOrAdmin &&
                (user == null || (!Db.Proposals.Any(p => p.ProjectId == id && p.ApplicantId == user.Id) &&
                    !Db.ContractMembers.Any(m => m.Contract.ProjectId == id && m.UserAccountId == user.Id)))) return HttpNotFound();
            var recruitmentOpen = project.Status == "Published" && RecruitmentPolicy.DateAllowsApplications(project.DeadlineUtc, DateTime.UtcNow);
            ViewBag.ProjectUpdatedAtUtc = LastProjectUpdate(id);
            ViewBag.CanEditProject = new ProjectEditingService(Db).IsEditable(project);
            ViewBag.RecruitmentOpen = recruitmentOpen;
            ViewBag.Assignments = Db.RoleAssignments.Where(a => a.ProjectId == id).ToList();
            ViewBag.ProjectCoreSkills = Db.ProjectSkillRequirements.Include(s => s.Skill).Where(s => s.ProjectId == id).ToList();
            int? workspaceId=null;
            if(user!=null)
            {
                var userId=user.Id;
                workspaceId=Db.Workspaces.Where(w=>w.ProjectId==id && (project.ClientId==userId ||
                    (w.ContractId.HasValue ? Db.ContractMembers.Any(m=>m.ContractId==w.ContractId && m.UserAccountId==userId) :
                    Db.TeamMembers.Any(m=>m.ProjectTeamId==w.ProjectTeamId && m.UserAccountId==userId))))
                    .Select(w=>(int?)w.Id).FirstOrDefault();
            }
            ViewBag.WorkspaceId = workspaceId;
            ViewBag.CanEnterWorkspace = workspaceId.HasValue && user != null &&
                (project.ClientId == user.Id || Db.TeamMembers.Any(m => m.Team.ProjectId == id && m.UserAccountId == user.Id));
            ViewBag.MyProposals = user == null ? new int[0] : Db.Proposals.Where(p => p.ProjectId == id && p.ApplicantId == user.Id).Select(p => p.RoleRequirementId).ToArray();
            ViewBag.CanApply = recruitmentOpen && user != null && user.Role == "Freelancer" && project.ProjectType != "TEAM" && user.Email != project.Client.Email && (user.GoogleSubject == null || user.GoogleSubject != project.Client.GoogleSubject);
            ViewBag.LeaderGroups = recruitmentOpen && user != null && user.Role == "Freelancer" && project.ProjectType != "SOLO"
                ? Db.FreelancerGroupMembers.Include(m => m.Group)
                    .Where(m => m.UserAccountId == user.Id && m.Status == "Active" && m.Group.LeaderId == user.Id)
                    .Select(m => m.Group).ToList()
                : new System.Collections.Generic.List<FreelancerGroup>();
            ViewBag.GroupOpportunities = user != null && user.Role == "Freelancer" && project.ProjectType != "SOLO" ? new GroupRecruitmentService(Db).Opportunities(project, user.Id) : new System.Collections.Generic.List<NexusFreelance.Web.ViewModels.GroupOpportunity>();
            return View(project);
        }

        [Authorize, HttpGet]
        public ActionResult Mine()
        {
            var user = CurrentUser;
            if (user == null || user.Role != "Client") return Forbidden();
            ViewBag.ProjectUpdates = Db.Notifications.Include(n => n.Actor).Where(n => n.RecipientId == user.Id && n.ProjectId.HasValue && !n.ReadAtUtc.HasValue).OrderByDescending(n => n.Id).Take(200).ToList().GroupBy(n => n.ProjectId.Value).ToDictionary(g => g.Key, g => g.ToList());
            return View(Db.Projects.Where(p => p.ClientId == user.Id).OrderByDescending(p => p.CreatedAtUtc).ToList());
        }

        [Authorize, HttpGet]
        public ActionResult Create()
        {
            if (CurrentUser?.Role != "Client") return Forbidden();
            SetSkillOptions();
            return View(new ProjectForm { ProjectType = "TEAM" });
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult Create(ProjectForm form)
        {
            var user = CurrentUser;
            if (user?.Role != "Client") return Forbidden();
            SetSkillOptions();
            ValidateProject(form);
            if (!ModelState.IsValid) return View(form ?? new ProjectForm { ProjectType = "TEAM" });
            var project = new ProjectEditingService(Db).Create(user.Id, form);
            TempData["Notice"] = "Đã tạo dự án. Hãy khai báo vị trí cần tuyển.";
            return RedirectToAction("Manage", new { id = project.Id });
        }

        [Authorize, HttpGet]
        public ActionResult Edit(int id)
        {
            var user = CurrentUser;
            var project = Db.Projects.Find(id);
            if (project == null) return HttpNotFound();
            if (user == null || user.Id != project.ClientId) return Forbidden();
            if (!new ProjectEditingService(Db).IsEditable(project)) return new HttpStatusCodeResult(409, "Dự án đang khóa chỉnh sửa do giai đoạn hiện tại hoặc đang có tranh chấp.");
            SetEditorContext(project);
            return View("Create", new ProjectForm { EditVersion = new ProjectEditingService(Db).Version(project), SkillIds = Db.ProjectSkillRequirements.Where(s => s.ProjectId == id).Select(s => s.SkillTrackId).ToArray(), MinimumSkillLevel = Db.ProjectSkillRequirements.Where(s => s.ProjectId == id).Select(s => (int?)s.MinimumLevel).Min() ?? 1, Title = project.Title, Description = project.Description, Industry = project.Industry, Budget = project.Budget, ProjectType = project.ProjectType, Deadline = project.DeadlineUtc });
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult Edit(int id, ProjectForm form)
        {
            var user = CurrentUser;
            var project = Db.Projects.Find(id);
            if (project == null) return HttpNotFound();
            if (user == null || project.ClientId != user.Id) return Forbidden();
            if (!new ProjectEditingService(Db).IsEditable(project)) return new HttpStatusCodeResult(409, "Dự án đang khóa chỉnh sửa do giai đoạn hiện tại hoặc đang có tranh chấp.");
            SetEditorContext(project);
            ValidateProject(form, (bool)ViewBag.InformationOnly);
            if (!ModelState.IsValid) return View("Create", form ?? new ProjectForm { ProjectType = "TEAM" });
            try
            {
                var result = new ProjectEditingService(Db).Save(id, user.Id, form);
                TempData["Notice"] = !result.Changed ? "Nội dung không thay đổi; không gửi lại thông báo." :
                    result.Recipients > 0 ? "Đã cập nhật dự án và thông báo cho " + result.Recipients + " người liên quan." : "Đã lưu thông tin dự án.";
            }
            catch (InvalidOperationException ex)
            {
                Db.Entry(project).Reload(); SetEditorContext(project);
                ModelState.AddModelError("", ex.Message);
                Response.StatusCode = 409; Response.TrySkipIisCustomErrors = true;
                return View("Create", form);
            }
            return RedirectToAction("Manage", new { id });
        }

        private void SetEditorContext(Project project)
        {
            SetSkillOptions();
            ViewBag.EditingProject = project;
            ViewBag.InformationOnly = new ProjectEditingService(Db).InformationOnly(project);
            ViewBag.FixedSkills = Db.ProjectSkillRequirements.Include(s => s.Skill).Where(s => s.ProjectId == project.Id).ToList();
        }

        private void SetSkillOptions() { ViewBag.SkillOptions = Db.SkillTracks.Where(t => t.IsActive != false).OrderBy(t => t.Title).Take(200).ToList(); }

        private DateTime? LastProjectUpdate(int id) => Db.ActivityLogs.Where(a => a.ProjectId == id &&
            (a.Action.StartsWith("Updated project information:") || a.Action == "Updated core skills for recruitment matching" || a.Action.StartsWith("Updated expected date")))
            .OrderByDescending(a => a.Id).Select(a => (DateTime?)a.CreatedAtUtc).FirstOrDefault();

        private void ValidateProject(ProjectForm form, bool informationOnly = false)
        {
            foreach (var issue in ProjectInputPolicy.Errors(form, Db, informationOnly))
                ModelState.AddModelError(issue.Key, issue.Value);
        }

        [Authorize, HttpGet]
        public ActionResult Manage(int id)
        {
            var user = CurrentUser;
            var project = Db.Projects.Include(p => p.Requirements).SingleOrDefault(p => p.Id == id);
            if (project == null) return HttpNotFound();
            if (user == null || user.Id != project.ClientId) return Forbidden();
            ViewBag.ProjectUpdatedAtUtc = LastProjectUpdate(id);
            ViewBag.CanEditProject = new ProjectEditingService(Db).IsEditable(project);
            var received = Db.Proposals.Include(p => p.Applicant).Include(p => p.Requirement)
                .Where(p => p.ProjectId == id).OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id).ToList();
            ViewBag.Proposals = received.Where(p => !p.GroupBidId.HasValue).ToList();
            var applicantIds = received.Select(p => p.ApplicantId).Distinct().ToArray();
            ViewBag.PublicApplicantIds = Db.FreelancerProfiles.Where(p => applicantIds.Contains(p.UserAccountId) &&
                p.IsPublic != false && !p.User.IsSuspended && p.User.Role == "Freelancer")
                .Select(p => p.UserAccountId).ToList();
            ViewBag.Assignments = Db.RoleAssignments.Include(a => a.Freelancer).Where(a => a.ProjectId == id).ToList();
            ViewBag.Workspace = Db.Workspaces.Where(w => w.ProjectId == id).OrderBy(w=>w.Id).FirstOrDefault();
            ViewBag.Reviews = Db.Reviews.Where(r => r.ProjectId == id).ToList();
            ViewBag.Recommendations = new SmartMatchingService(Db).ForProject(project);
            SetSkillOptions();
            var bids = Db.GroupBids.Include(b => b.Group).Include(b => b.Leader)
                .Where(b => b.ProjectId == id).OrderByDescending(b => b.CreatedAtUtc).ThenByDescending(b => b.Id).ToList();
            ViewBag.GroupBids = bids;
            ViewBag.GroupBidProposals = received.Where(p => p.GroupBidId.HasValue).ToList();
            var groupIds = bids.Select(b => b.FreelancerGroupId).Distinct().ToArray();
            ViewBag.PublicGroupIds = Db.FreelancerGroups.Where(g => groupIds.Contains(g.Id) && g.IsPublic != false &&
                !g.Leader.IsSuspended && g.Leader.Role == "Freelancer" &&
                !Db.FreelancerProfiles.Any(p => p.UserAccountId == g.LeaderId && p.IsPublic == false))
                .Select(g => g.Id).ToList();
            return View(project);
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult UpdateSkills(int id, int[] skillIds, int minimumSkillLevel = 1)
        {
            var user = CurrentUser;
            skillIds = skillIds ?? new int[0];
            if (!ModelState.IsValid || skillIds.Length > 12 || skillIds.Distinct().Count() != skillIds.Length || minimumSkillLevel < 1 || minimumSkillLevel > 5 || Db.SkillTracks.Count(t => skillIds.Contains(t.Id) && t.IsActive != false) != skillIds.Length)
            { TempData["Error"] = "Chọn tối đa 12 kỹ năng hoạt động và mức yêu cầu từ 1 đến 5."; return RedirectToAction("Manage", new { id }); }
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var locked = Db.Database.SqlQuery<int>("SELECT Id FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", id).SingleOrDefault();
                if (locked == 0) return HttpNotFound();
                var project = Db.Projects.Single(p => p.Id == id);
                if (user == null || project.ClientId != user.Id) return Forbidden();
                if (!new ProjectEditingService(Db).IsEditable(project) || (project.Status != "Draft" && project.Status != "Published" && project.Status != "Paused")) return new HttpStatusCodeResult(409, "Chỉ cập nhật gợi ý khi còn tuyển người và không có tranh chấp.");
                var previous = Db.ProjectSkillRequirements.Where(s => s.ProjectId == id).ToList();
                var changed = !skillIds.OrderBy(s => s).SequenceEqual(previous.Select(s => s.SkillTrackId).OrderBy(s => s)) || previous.Any(s => s.MinimumLevel != minimumSkillLevel);
                Db.ProjectSkillRequirements.RemoveRange(previous.Where(s => !skillIds.Contains(s.SkillTrackId)));
                foreach (var skill in previous.Where(s => skillIds.Contains(s.SkillTrackId))) skill.MinimumLevel = minimumSkillLevel;
                foreach (var skillId in skillIds.Except(previous.Select(s => s.SkillTrackId))) Db.ProjectSkillRequirements.Add(new ProjectSkillRequirement { ProjectId = id, SkillTrackId = skillId, MinimumLevel = minimumSkillLevel });
                if (changed) {
                    Db.ActivityLogs.Add(new ActivityLog { ProjectId = id, ActorId = user.Id, Action = "Updated core skills for recruitment matching", CreatedAtUtc = DateTime.UtcNow });
                    new ProjectEditingService(Db).NotifyParticipants(project, user.Id, new[] { "kỹ năng tuyển dụng" });
                }
                Db.SaveChanges(); tx.Commit();
            }
            TempData["Notice"] = "Đã cập nhật core skill cho ghép nối ứng viên và nhóm.";
            return RedirectToAction("Manage", new { id });
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult AddRequirement(RequirementForm form)
        {
            var user = CurrentUser;
            if (form == null || form.ProjectId <= 0) return new HttpStatusCodeResult(400);
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(form.Title))
            {
                TempData["Error"] = "Tên vị trí và số lượng (1–50) phải hợp lệ.";
                return RedirectToAction("Manage", new { id = form.ProjectId });
            }
            try
            {
                using (var tx = Db.Database.BeginTransaction(IsolationLevel.Serializable))
                {
                    var locked = Db.Database.SqlQuery<int>(
                        "SELECT [Id] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", form.ProjectId).SingleOrDefault();
                    if (locked == 0) return HttpNotFound();
                    var project = Db.Projects.Single(p => p.Id == form.ProjectId);
                    if (user == null || user.Id != project.ClientId) return Forbidden();
                    if (project.Status != "Draft") return new HttpStatusCodeResult(409);
                    var roleTitle = form.Title.Trim();
                    if (Db.RoleRequirements.Any(r => r.ProjectId == project.Id && r.Title == roleTitle))
                    {
                        TempData["Error"] = "Vị trí đã tồn tại.";
                        return RedirectToAction("Manage", new { id = project.Id });
                    }
                    Db.RoleRequirements.Add(new RoleRequirement { ProjectId = project.Id, Title = roleTitle, RequiredCount = form.RequiredCount });
                    Db.SaveChanges();
                    tx.Commit();
                }
            }
            catch (System.Data.Entity.Infrastructure.DbUpdateException) { TempData["Error"] = "Vị trí đã tồn tại hoặc dữ liệu vừa thay đổi."; }
            catch (System.Data.SqlClient.SqlException) { TempData["Error"] = "Không thể lưu vị trí lúc này. Hãy thử lại."; }
            return RedirectToAction("Manage", new { id = form.ProjectId });
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult Publish(int id)
        {
            var user = CurrentUser;
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                var locked = Db.Database.SqlQuery<int>(
                    "SELECT [Id] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", id).SingleOrDefault();
                if (locked == 0) return HttpNotFound();
                var project = Db.Projects.Single(p => p.Id == id);
                if (user == null || user.Id != project.ClientId) return Forbidden();
                if (project.Status != "Draft" || !Db.RoleRequirements.Any(r => r.ProjectId == id)) return new HttpStatusCodeResult(409);
                if (!RecruitmentPolicy.DateAllowsApplications(project.DeadlineUtc, DateTime.UtcNow)) return new HttpStatusCodeResult(409, "Cập nhật hạn dự kiến trước khi công bố dự án.");
                project.Status = "Published";
                project.PublishedAtUtc = project.PublishedAtUtc ?? DateTime.UtcNow;
                Db.ActivityLogs.Add(new ActivityLog { ProjectId = id, ActorId = user.Id, Action = "Published project", CreatedAtUtc = DateTime.UtcNow });
                Db.SaveChanges();
                tx.Commit();
            }
            return RedirectToAction("Manage", new { id });
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult UpdateDeadline(int id, DateTime? deadline)
        {
            if (CurrentUser?.Role != "Client") return Forbidden();
            if (!ModelState.IsValid || !deadline.HasValue || deadline.Value.Date < DateTime.UtcNow.Date || deadline.Value.Date > DateTime.UtcNow.Date.AddYears(5))
            { TempData["Error"] = "Chọn hạn dự kiến từ hôm nay đến tối đa 5 năm tới."; return RedirectToAction("Manage", new { id }); }
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var locked = Db.Database.SqlQuery<int>("SELECT Id FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", id).SingleOrDefault();
                if (locked == 0) return HttpNotFound();
                var project = Db.Projects.Single(p => p.Id == id);
                if (project.ClientId != CurrentUser.Id) return Forbidden();
                if (!new ProjectEditingService(Db).IsEditable(project) || (project.Status != "Draft" && project.Status != "Published" && project.Status != "Paused")) return new HttpStatusCodeResult(409, "Chỉ đổi hạn khi còn tuyển người và không có tranh chấp.");
                var date = DateTime.SpecifyKind(deadline.Value.Date, DateTimeKind.Utc);
                if (project.DeadlineUtc != date)
                {
                    project.DeadlineUtc = date;
                    Db.ActivityLogs.Add(new ActivityLog { ProjectId=id, ActorId=CurrentUser.Id, Action="Updated expected date to " + date.ToString("yyyy-MM-dd"), CreatedAtUtc=DateTime.UtcNow });
                    new ProjectEditingService(Db).NotifyParticipants(project, CurrentUser.Id, new[] { "hạn dự kiến " + date.ToString("dd/MM/yyyy") });
                    Db.SaveChanges();
                }
                tx.Commit();
            }
            TempData["Notice"] = "Đã cập nhật hạn dự kiến. Thời hạn và mốc của hợp đồng đã xác nhận được giữ nguyên.";
            return RedirectToAction("Manage", new { id });
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult ChangeStatus(int id, string operation)
        {
            if (CurrentUser?.Role != "Client") return Forbidden();
            try
            {
                new ProjectWorkflowService(Db).ChangeProjectStatus(id, CurrentUser.Id, operation);
                TempData["Notice"] = "Đã cập nhật trạng thái dự án.";
            }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            catch (System.Data.SqlClient.SqlException) { TempData["Error"] = "Trạng thái vừa thay đổi. Hãy thử lại."; }
            return RedirectToAction("Manage", new { id });
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult Accept(int id, int proposalId)
        {
            if (CurrentUser?.Role != "Client") return Forbidden();
            try { new ProjectWorkflowService(Db).AcceptProposal(id, proposalId, CurrentUser.Id); TempData["Notice"] = "Đã nhận ứng viên vào đội."; }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            catch (System.Data.Entity.Infrastructure.DbUpdateException) { TempData["Error"] = "Không thể nhận ứng viên do dữ liệu đã thay đổi. Hãy tải lại trang."; }
            catch (System.Data.SqlClient.SqlException) { TempData["Error"] = "Hệ thống đang xử lý đồng thời. Hãy tải lại trạng thái tuyển dụng."; }
            return RedirectToAction("Manage", new { id });
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult Shortlist(int id, int proposalId)
        {
            if (CurrentUser.Role != "Client") return Forbidden();
            try { new ProjectWorkflowService(Db).Shortlist(id, proposalId, CurrentUser.Id); TempData["Notice"] = "Đã chọn sơ bộ và thông báo cho ứng viên."; }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            catch (System.Data.SqlClient.SqlException) { TempData["Error"] = "Chào giá đang được xử lý, hãy tải lại."; }
            return RedirectToAction("Manage", new { id });
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reject(int id, int proposalId)
        {
            if (CurrentUser?.Role != "Client") return Forbidden();
            try
            {
                new ProjectWorkflowService(Db).RejectProposal(id, proposalId, CurrentUser.Id);
                TempData["Notice"] = "Đã từ chối đề xuất.";
            }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            catch (System.Data.Entity.Infrastructure.DbUpdateException) { TempData["Error"] = "Proposal vừa được cập nhật. Hãy tải lại trang."; }
            catch (System.Data.SqlClient.SqlException) { TempData["Error"] = "Hệ thống đang xử lý đồng thời. Hãy thử lại."; }
            return RedirectToAction("Manage", new { id });
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult LockTeam(int id)
        {
            if (CurrentUser?.Role != "Client") return Forbidden();
            try { new ProjectWorkflowService(Db).LockTeamAndCreateWorkspace(id, CurrentUser.Id); TempData["Notice"] = "Đội đã khóa và Workspace được mở."; }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            catch (System.Data.Entity.Infrastructure.DbUpdateException) { TempData["Error"] = "Không thể khóa đội do dữ liệu vừa thay đổi."; }
            return RedirectToAction("Manage", new { id });
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult Complete(int id)
        {
            if (CurrentUser?.Role != "Client") return Forbidden();
            try { new ProjectWorkflowService(Db).CompleteProject(id, CurrentUser.Id); TempData["Notice"] = "Dự án hoàn thành."; }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            return RedirectToAction("Manage", new { id });
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult Review(ReviewForm form)
        {
            if (CurrentUser?.Role != "Client") return Forbidden();
            if (form == null || form.ProjectId <= 0) return new HttpStatusCodeResult(400);
            if (!ModelState.IsValid) TempData["Error"] = "Đánh giá không hợp lệ.";
            else
                try { new ProjectWorkflowService(Db).AddReview(form.ProjectId, form.FreelancerId, CurrentUser.Id, form.Rating, form.Comment); TempData["Notice"] = "Đã lưu đánh giá."; }
                catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            return RedirectToAction("Manage", new { id = form.ProjectId });
        }
    }
}
