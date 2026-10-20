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
    [Authorize]
    public class WorkspaceController : BaseController
    {
        private ContractService Contracts => new ContractService(Db);
        private bool CanAccess(Workspace w,int userId) => Contracts.CanAccess(w,userId);
        private bool CanWork(Workspace w) => Contracts.CanWork(w);

        [HttpGet]
        public ActionResult Index(int id, int? beforeMessageId, int? beforeFileVersionId)
        {
            var user = CurrentUser;
            if (user == null) return Forbidden();
            var workspace = Db.Workspaces.Include(w => w.Project).SingleOrDefault(w => w.Id == id);
            if (workspace == null) return HttpNotFound();
            if (!CanAccess(workspace, user.Id) && user.Role != "Admin") return Forbidden();
            ViewBag.EscrowMilestones = Db.EscrowMilestones.Where(m => m.ProjectId == workspace.ProjectId && m.ContractId == workspace.ContractId && (m.Status == "Funded" || m.Status == "RevisionsRequested")).OrderBy(m => m.Id).ToList();
            ViewBag.LegacyWithoutEscrow = !workspace.ContractId.HasValue && !Db.EscrowMilestones.Any(m => m.ProjectId == workspace.ProjectId && m.ContractId == null);
            ViewBag.Tasks = Db.Tasks.Include(t => t.Assignee).Where(t => t.WorkspaceId == id).OrderBy(t => t.Id).ToList();
            ViewBag.TaskHistories = Db.TaskHistories.Include(h => h.Actor).Where(h => h.Task.WorkspaceId == id)
                .OrderByDescending(h => h.Id).Take(100).ToList();
            ViewBag.Deliverables = Db.Deliverables.Include(d => d.SubmittedBy).Include(d=>d.EscrowMilestone).Where(d => d.WorkspaceId == id).OrderByDescending(d => d.Id).ToList();
            if (beforeFileVersionId.HasValue && beforeFileVersionId.Value <= 0)
                return new HttpStatusCodeResult(400);
            // Project only metadata: never load binary data to render the workspace.
            var fileQuery = Db.WorkspaceFileVersions.AsNoTracking()
                .Where(v => v.File.WorkspaceId == id);
            if (beforeFileVersionId.HasValue)
                fileQuery = fileQuery.Where(v => v.Id < beforeFileVersionId.Value);
            var fileVersions = fileQuery.OrderByDescending(v => v.Id)
                .Take(30).Select(v => new WorkspaceFileRow
                {
                    FileId = v.WorkspaceFileId, VersionId = v.Id,
                    VersionNumber = v.VersionNumber,
                    IsLatest = !Db.WorkspaceFileVersions.Any(next =>
                        next.WorkspaceFileId == v.WorkspaceFileId && next.VersionNumber > v.VersionNumber),
                    FileName = v.FileName,
                    UploadedByName = v.UploadedBy.Name, ByteLength = v.ByteLength,
                    CreatedAtUtc = v.CreatedAtUtc
                }).ToList();
            ViewBag.FileVersions = fileVersions;
            ViewBag.FilePageBefore = beforeFileVersionId;
            var oldestFileVersionId = fileVersions.Count == 0 ? 0 : fileVersions[fileVersions.Count - 1].VersionId;
            ViewBag.HasOlderFileVersions = oldestFileVersionId > 0 &&
                Db.WorkspaceFileVersions.Any(v => v.File.WorkspaceId == id && v.Id < oldestFileVersionId);
            ViewBag.Members = Db.TeamMembers.Include(m => m.User).Where(m => m.ProjectTeamId == workspace.ProjectTeamId && (!workspace.ContractId.HasValue || Db.ContractMembers.Any(c=>c.ContractId==workspace.ContractId && c.UserAccountId==m.UserAccountId))).ToList();
            ViewBag.Milestones = Db.ProjectMilestones.Where(m => m.WorkspaceId == id).OrderBy(m => m.DueAtUtc).ThenBy(m => m.Id).Take(100).ToList();
            ViewBag.Meetings = Db.WorkspaceMeetings.Where(m => m.WorkspaceId == id).OrderByDescending(m => m.StartsAtUtc).ThenByDescending(m => m.Id).Take(100).ToList();
            if (beforeMessageId.HasValue && beforeMessageId.Value <= 0) return new HttpStatusCodeResult(400);
            var messageQuery = Db.WorkspaceMessages.Include(m => m.Author).Where(m => m.WorkspaceId == id);
            if (beforeMessageId.HasValue) messageQuery = messageQuery.Where(m => m.Id < beforeMessageId.Value);
            var messages = messageQuery.OrderByDescending(m => m.Id).Take(50).ToList();
            messages.Reverse();
            ViewBag.Messages = messages;
            // Capture the bound outside the LINQ expression: EF6 cannot translate List<T>.get_Item.
            var oldestMessageId = messages.Count == 0 ? 0 : messages[0].Id;
            ViewBag.HasOlderMessages = oldestMessageId > 0 &&
                Db.WorkspaceMessages.Any(m => m.WorkspaceId == id && m.Id < oldestMessageId);
            ViewBag.IsClient = workspace.Project.ClientId == user.Id;
            ViewBag.CanSubmit = Contracts.CanSubmit(workspace,user.Id);
            ViewBag.WorkStatus=workspace.ContractId.HasValue?Db.Contracts.Where(c=>c.Id==workspace.ContractId).Select(c=>c.Status).Single():workspace.Project.Status;
            ViewBag.IsAudit = user.Role == "Admin";
            ViewBag.CanManageWork = Contracts.CanManageWork(workspace, user.Id);
            ViewBag.CanWork=CanWork(workspace) && user.Role != "Admin";
            ViewBag.CanDiscuss=Contracts.CanDiscuss(workspace) && CanAccess(workspace, user.Id) && user.Role != "Admin";
            ViewBag.CurrentUserId = user.Id;
            return View(workspace);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateMilestone(MilestoneForm form)
        {
            if (form == null) return new HttpStatusCodeResult(400);
            var user = CurrentUser;
            var workspace = Db.Workspaces.Include(w => w.Project).SingleOrDefault(w => w.Id == form.WorkspaceId);
            if (workspace == null) return HttpNotFound();
            if (user == null || !Contracts.CanManageWork(workspace, user.Id)) return Forbidden();
            if (!CanWork(workspace)) return new HttpStatusCodeResult(409);
            var title = (form.Title ?? "").Trim();
            var due = form.DueAtUtc.HasValue ? DateTime.SpecifyKind(form.DueAtUtc.Value, DateTimeKind.Utc) : DateTime.MinValue;
            if (!ModelState.IsValid || title.Length == 0 || title.Length > 180 ||
                due < DateTime.UtcNow.AddMinutes(-5) || due > DateTime.UtcNow.AddYears(5))
            {
                TempData["Error"] = "Mốc tiến độ phải có tiêu đề và hạn UTC hợp lệ trong tương lai.";
                return RedirectToAction("Index", new { id = workspace.Id });
            }
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var current = Db.Database.SqlQuery<string>(
                    "SELECT [Status] FROM [dbo].[Projects] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]=@p0", workspace.ProjectId).Single();
                if (!CanWork(workspace)) return new HttpStatusCodeResult(409);
                Db.ProjectMilestones.Add(new ProjectMilestone { WorkspaceId = workspace.Id,
                    Title = title, DueAtUtc = due, Status = "Planned", CreatedAtUtc = DateTime.UtcNow });
                Db.ActivityLogs.Add(new ActivityLog { ProjectId = workspace.ProjectId, ActorId = user.Id,
                    Action = "Created milestone: " + title, CreatedAtUtc = DateTime.UtcNow });
                Db.SaveChanges();
                tx.Commit();
            }
            return RedirectToAction("Index", new { id = workspace.Id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CompleteMilestone(int id)
        {
            var user = CurrentUser;
            var milestone = Db.ProjectMilestones.Include(m => m.Workspace.Project).SingleOrDefault(m => m.Id == id);
            if (milestone == null) return HttpNotFound();
            if (user == null || !Contracts.CanManageWork(milestone.Workspace, user.Id)) return Forbidden();
            if (!CanWork(milestone.Workspace)) return new HttpStatusCodeResult(409);
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var current = Db.Database.SqlQuery<string>(
                    "SELECT [Status] FROM [dbo].[Projects] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]=@p0", milestone.Workspace.ProjectId).Single();
                if (!CanWork(milestone.Workspace)) return new HttpStatusCodeResult(409);
                Db.Database.SqlQuery<int>(
                    "SELECT [Id] FROM [dbo].[ProjectMilestones] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]=@p0", id).Single();
                Db.Entry(milestone).Reload();
                if (milestone.Status != "Planned") return new HttpStatusCodeResult(409);
                milestone.Status = "Completed";
                milestone.CompletedAtUtc = DateTime.UtcNow;
                Db.ActivityLogs.Add(new ActivityLog { ProjectId = milestone.Workspace.ProjectId, ActorId = user.Id,
                    Action = "Completed milestone #" + id, CreatedAtUtc = DateTime.UtcNow });
                Db.SaveChanges();
                tx.Commit();
            }
            return RedirectToAction("Index", new { id = milestone.WorkspaceId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult ScheduleMeeting(MeetingForm form)
        {
            if (form == null) return new HttpStatusCodeResult(400);
            var user = CurrentUser;
            var workspace = Db.Workspaces.Include(w => w.Project).SingleOrDefault(w => w.Id == form.WorkspaceId);
            if (workspace == null) return HttpNotFound();
            if (user == null || (workspace.Project.ClientId != user.Id && !Contracts.CanManageWork(workspace,user.Id))) return Forbidden();
            if (!CanWork(workspace)) return new HttpStatusCodeResult(409);
            var title = (form.Title ?? "").Trim();
            var url = (form.Url ?? "").Trim();
            var when = form.StartsAtUtc.HasValue ? DateTime.SpecifyKind(form.StartsAtUtc.Value, DateTimeKind.Utc) : DateTime.MinValue;
            Uri uri;
            if (!ModelState.IsValid || title.Length == 0 || title.Length > 180 ||
                !Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps ||
                when < DateTime.UtcNow.AddMinutes(-5) || when > DateTime.UtcNow.AddYears(5))
            {
                TempData["Error"] = "Lịch họp yêu cầu tiêu đề, liên kết HTTPS và giờ UTC hợp lệ.";
                return RedirectToAction("Index", new { id = workspace.Id });
            }
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var current = Db.Database.SqlQuery<string>(
                    "SELECT [Status] FROM [dbo].[Projects] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]=@p0", workspace.ProjectId).Single();
                if (!CanWork(workspace)) return new HttpStatusCodeResult(409);
                Db.WorkspaceMeetings.Add(new WorkspaceMeeting { WorkspaceId = workspace.Id,
                    Title = title, Url = url, StartsAtUtc = when, CreatedAtUtc = DateTime.UtcNow });
                var memberIds = Db.TeamMembers.Where(m => m.ProjectTeamId == workspace.ProjectTeamId && (!workspace.ContractId.HasValue || Db.ContractMembers.Any(c=>c.ContractId==workspace.ContractId && c.UserAccountId==m.UserAccountId)))
                    .Select(m => m.UserAccountId).Distinct().ToList();
                foreach (var recipient in memberIds.Concat(new[] {workspace.Project.ClientId}).Distinct().Where(id => id != user.Id))
                    NotificationService.Queue(Db, recipient, "Lịch họp mới: " + title.Substring(0, Math.Min(title.Length, 100)),
                        "/Workspace/Index/" + workspace.Id);
                Db.ActivityLogs.Add(new ActivityLog { ProjectId = workspace.ProjectId, ActorId = user.Id,
                    Action = "Scheduled meeting: " + title, CreatedAtUtc = DateTime.UtcNow });
                Db.SaveChanges();
                tx.Commit();
            }
            return RedirectToAction("Index", new { id = workspace.Id });
        }

        // Chat is plain text: Razor and realtime textContent always encode literal markup.
        [HttpPost, ValidateAntiForgeryToken, ValidateInput(false)]
        public ActionResult SendMessage(WorkspaceMessageForm form)
        {
            if (form == null) return new HttpStatusCodeResult(400);
            var user = CurrentUser;
            if (user == null) return Forbidden();
            var workspace = Db.Workspaces.Include(w => w.Project).SingleOrDefault(w => w.Id == form.WorkspaceId);
            if (workspace == null) return HttpNotFound();
            if (!CanAccess(workspace, user.Id)) return Forbidden();
            if (!Contracts.CanDiscuss(workspace)) return new HttpStatusCodeResult(409, "Kênh trao đổi đã đóng; lịch sử vẫn được lưu.");
            string body = (form.Body ?? "").Trim();
            if (!ModelState.IsValid || body.Length == 0 || body.Length > 2000)
            {
                if (Request.IsAjaxRequest()) { Response.StatusCode = 422; return Json(new { error = "Nhập tin nhắn từ 1 đến 2000 ký tự." }); }
                TempData["Error"] = "Tin nhắn phải có nội dung và dài tối đa 2.000 ký tự.";
                return RedirectToAction("Index", new { id = workspace.Id });
            }
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                // All project mutations lock the project first, before child records.
                var currentStatus = Db.Database.SqlQuery<string>(
                    "SELECT [Status] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0",
                    workspace.ProjectId).Single();
                Db.Entry(workspace.Project).Reload();
                if (!Contracts.CanDiscuss(workspace)) return new HttpStatusCodeResult(409, "Kênh trao đổi đã đóng.");
                var since = DateTime.UtcNow.AddMinutes(-1);
                if (Db.WorkspaceMessages.Count(m => m.WorkspaceId == workspace.Id && m.AuthorId == user.Id && m.CreatedAtUtc >= since) >= 30) return new HttpStatusCodeResult(429);
                Db.WorkspaceMessages.Add(new WorkspaceMessage
                {
                    WorkspaceId = workspace.Id, AuthorId = user.Id,
                    Body = body, CreatedAtUtc = DateTime.UtcNow
                });
                var recipients = workspace.ContractId.HasValue ? Db.ContractMembers.Where(m => m.ContractId == workspace.ContractId).Select(m => m.UserAccountId).ToList() :
                    Db.TeamMembers.Where(m => m.ProjectTeamId == workspace.ProjectTeamId).Select(m => m.UserAccountId).ToList();
                foreach (var recipient in recipients.Concat(new[] {workspace.Project.ClientId}).Distinct().Where(id => id != user.Id))
                    NotificationService.Queue(Db, recipient, "Trao đổi mới từ " + user.Name, "/Workspace/Index/" + workspace.Id, user.Id, workspace.ProjectId);
                Db.SaveChanges();
                tx.Commit();
            }
            if (Request.IsAjaxRequest()) return Json(new { sent = true });
            return RedirectToAction("Index", new { id = workspace.Id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateTask(TaskForm form)
        {
            if (form == null) return new HttpStatusCodeResult(400);
            var user = CurrentUser;
            var workspace = Db.Workspaces.Include(w => w.Project).SingleOrDefault(w => w.Id == form.WorkspaceId);
            if (workspace == null) return HttpNotFound();
            if (user == null || !Contracts.CanManageWork(workspace, user.Id)) return Forbidden();
            if (!CanWork(workspace)) return new HttpStatusCodeResult(409);
            var title = (form.Title ?? "").Trim();
            var priority = string.IsNullOrWhiteSpace(form.Priority) ? "Normal" : form.Priority.Trim();
            var due = form.DueAtUtc.HasValue ? DateTime.SpecifyKind(form.DueAtUtc.Value, DateTimeKind.Utc) : (DateTime?)null;
            if (!ModelState.IsValid || title.Length == 0 || title.Length > 200 ||
                (priority != "Low" && priority != "Normal" && priority != "High") ||
                (due.HasValue && (due.Value < DateTime.UtcNow.AddMinutes(-5) || due.Value > DateTime.UtcNow.AddYears(5))) ||
                !Contracts.IsMember(workspace,form.AssigneeId))
            { TempData["Error"] = "Task hoặc thành viên được giao không hợp lệ."; return RedirectToAction("Index", new { id = workspace.Id }); }
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var currentStatus = Db.Database.SqlQuery<string>(
                    "SELECT [Status] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0",
                    workspace.ProjectId).Single();
                if (!CanWork(workspace)) return new HttpStatusCodeResult(409);
                var task = new WorkTask { WorkspaceId = workspace.Id, AssigneeId = form.AssigneeId,
                    Title = title, Priority = priority, DueAtUtc = due, Status = "ToDo" };
                Db.Tasks.Add(task);
                Db.TaskHistories.Add(new TaskHistory { Task = task, ActorId = user.Id,
                    EventType = "Created", NewStatus = "ToDo", NewTitle = title,
                    NewAssigneeId = form.AssigneeId, NewPriority = priority, NewDueAtUtc = due,
                    CreatedAtUtc = DateTime.UtcNow });
                NotificationService.Queue(Db, form.AssigneeId, "Bạn được giao Task mới: " + title.Substring(0, Math.Min(title.Length, 100)), "/Workspace/Index/" + workspace.Id);
                Db.SaveChanges();
                tx.Commit();
            }
            return RedirectToAction("Index", new { id = workspace.Id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditTask(TaskEditForm form)
        {
            if (form == null) return new HttpStatusCodeResult(400);
            var user = CurrentUser;
            var task = Db.Tasks.Include(t => t.Workspace.Project).SingleOrDefault(t => t.Id == form.Id);
            if (task == null) return HttpNotFound();
            var workspace = task.Workspace;
            if (user == null || !Contracts.CanManageWork(workspace, user.Id)) return Forbidden();
            if (!CanWork(workspace)) return new HttpStatusCodeResult(409);
            var title = (form.Title ?? "").Trim();
            var priority = (form.Priority ?? "").Trim();
            var due = form.DueAtUtc.HasValue ? DateTime.SpecifyKind(form.DueAtUtc.Value, DateTimeKind.Utc) : (DateTime?)null;
            if (!ModelState.IsValid || title.Length == 0 || title.Length > 200 ||
                (priority != "Low" && priority != "Normal" && priority != "High") ||
                (due.HasValue && (due.Value < DateTime.UtcNow.AddMinutes(-5) || due.Value > DateTime.UtcNow.AddYears(5))))
            {
                TempData["Error"] = "Thông tin Task không hợp lệ.";
                return RedirectToAction("Index", new { id = workspace.Id });
            }
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                // Project-first lock order matches task creation, status updates and project completion.
                var projectStatus = Db.Database.SqlQuery<string>(
                    "SELECT [Status] FROM [dbo].[Projects] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]=@p0",
                    workspace.ProjectId).Single();
                if (!CanWork(workspace)) return new HttpStatusCodeResult(409);
                Db.Database.SqlQuery<int>(
                    "SELECT [Id] FROM [dbo].[Tasks] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]=@p0",
                    task.Id).Single();
                Db.Entry(task).Reload();
                if (!Contracts.IsMember(workspace,form.AssigneeId))
                {
                    TempData["Error"] = "Thành viên phụ trách không thuộc nhóm dự án.";
                    return RedirectToAction("Index", new { id = workspace.Id });
                }
                var oldPriority = task.Priority ?? "Normal";
                if (task.Title == title && task.AssigneeId == form.AssigneeId &&
                    oldPriority == priority && task.DueAtUtc == due)
                {
                    TempData["Error"] = "Không có thay đổi nào cần lưu.";
                    return RedirectToAction("Index", new { id = workspace.Id });
                }
                Db.TaskHistories.Add(new TaskHistory
                {
                    WorkTaskId = task.Id, ActorId = user.Id, EventType = "Edited",
                    OldStatus = task.Status, NewStatus = task.Status,
                    OldTitle = task.Title, NewTitle = title,
                    OldAssigneeId = task.AssigneeId, NewAssigneeId = form.AssigneeId,
                    OldPriority = oldPriority, NewPriority = priority,
                    OldDueAtUtc = task.DueAtUtc, NewDueAtUtc = due,
                    CreatedAtUtc = DateTime.UtcNow
                });
                var assigneeChanged = task.AssigneeId != form.AssigneeId;
                task.Title = title;
                task.AssigneeId = form.AssigneeId;
                task.Priority = priority;
                task.DueAtUtc = due;
                Db.ActivityLogs.Add(new ActivityLog { ProjectId = workspace.ProjectId, ActorId = user.Id,
                    Action = "Edited task #" + task.Id, CreatedAtUtc = DateTime.UtcNow });
                if (assigneeChanged)
                    NotificationService.Queue(Db, form.AssigneeId,
                        "Bạn được giao Task: " + title.Substring(0, Math.Min(100, title.Length)),
                        "/Workspace/Index/" + workspace.Id);
                Db.SaveChanges();
                tx.Commit();
            }
            return RedirectToAction("Index", new { id = workspace.Id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult UpdateTask(int id, string status)
        {
            var user = CurrentUser;
            var task = Db.Tasks.Include(t => t.Workspace.Project).SingleOrDefault(t => t.Id == id);
            if (task == null) return HttpNotFound();
            if (user == null || !CanAccess(task.Workspace, user.Id) || (task.AssigneeId != user.Id && !Contracts.CanManageWork(task.Workspace, user.Id))) return Forbidden();
            if (!CanWork(task.Workspace)) return new HttpStatusCodeResult(409);
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var currentStatus = Db.Database.SqlQuery<string>(
                    "SELECT [Status] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0",
                    task.Workspace.ProjectId).Single();
                if (!CanWork(task.Workspace)) return new HttpStatusCodeResult(409);
                Db.Entry(task).Reload();
                // Re-check the current assignee under the project lock. Ownership may
                // have changed since the initial read while this request was waiting.
                if (task.AssigneeId != user.Id && !Contracts.CanManageWork(task.Workspace, user.Id))
                    return Forbidden();
                var states = new[] { "ToDo", "Doing", "Review", "Done" };
                var oldPosition = Array.IndexOf(states, task.Status);
                var newPosition = Array.IndexOf(states, status);
                if (newPosition < 0 || oldPosition < 0 || Math.Abs(newPosition - oldPosition) != 1)
                    return new HttpStatusCodeResult(400);
                var previousStatus = task.Status;
                task.Status = status;
                Db.TaskHistories.Add(new TaskHistory { WorkTaskId = task.Id, ActorId = user.Id,
                    EventType = "StatusChanged", OldStatus = previousStatus, NewStatus = status,
                    CreatedAtUtc = DateTime.UtcNow });
                Db.ActivityLogs.Add(new ActivityLog { ProjectId = task.Workspace.ProjectId, ActorId = user.Id,
                    Action = "Updated task #" + task.Id + " to " + status, CreatedAtUtc = DateTime.UtcNow });
                Db.SaveChanges();
                tx.Commit();
            }
            return RedirectToAction("Index", new { id = task.WorkspaceId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Submit(DeliverableForm form)
        {
            if (form == null) return new HttpStatusCodeResult(400);
            var user = CurrentUser;
            var workspace = Db.Workspaces.Include(w => w.Project).SingleOrDefault(w => w.Id == form.WorkspaceId);
            if (workspace == null) return HttpNotFound();
            if (user == null || !Contracts.IsMember(workspace,user.Id)) return Forbidden();
            if (!Contracts.CanSubmit(workspace,user.Id)) return Forbidden();
            if (!CanWork(workspace)) return new HttpStatusCodeResult(409);
            Uri uri;
            if (!ModelState.IsValid || !Uri.TryCreate(form.Url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
            { TempData["Error"] = "Tên hoặc liên kết HTTPS bàn giao không hợp lệ."; return RedirectToAction("Index", new { id = workspace.Id }); }
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var currentStatus = Db.Database.SqlQuery<string>(
                    "SELECT [Status] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", workspace.ProjectId).Single();
                if (!CanWork(workspace)) return new HttpStatusCodeResult(409);
                if (form.RevisesId.HasValue)
                {
                    var previousId = form.RevisesId.Value;
                    var lockedId = Db.Database.SqlQuery<int>(
                        "SELECT [Id] FROM [dbo].[Deliverables] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", previousId)
                        .SingleOrDefault();
                    if (lockedId == 0) return new HttpStatusCodeResult(400);
                    var previous = Db.Deliverables.Single(d => d.Id == previousId);
                    if (previous.WorkspaceId != workspace.Id || previous.SubmittedById != user.Id) return Forbidden();
                    if (previous.Status != "RevisionsRequested" || Db.Deliverables.Any(d => d.RevisesId == previousId))
                        return new HttpStatusCodeResult(409, "Bản bàn giao này đã được thay thế hoặc chưa yêu cầu sửa.");
                }
                NexusFreelance.Web.Models.EscrowMilestone escrow;
                try { escrow = new WalletService(Db).SubmissionMilestone(workspace.ProjectId, form.EscrowMilestoneId, form.RevisesId.HasValue, workspace.ContractId); }
                catch (InvalidOperationException ex) { return new HttpStatusCodeResult(409,ex.Message); }
                if (form.RevisesId.HasValue)
                {
                    var parent = Db.Deliverables.Single(d => d.Id == form.RevisesId.Value);
                    if (parent.EscrowMilestoneId != escrow?.Id) return new HttpStatusCodeResult(409);
                }
                Db.Deliverables.Add(new Deliverable { EscrowMilestoneId = escrow?.Id, WorkspaceId = workspace.Id, SubmittedById = user.Id,
                    RevisesId = form.RevisesId, Title = form.Title.Trim(), Url = form.Url.Trim(),
                    Status = "Submitted", CreatedAtUtc = DateTime.UtcNow });
                Db.ActivityLogs.Add(new ActivityLog { ProjectId = workspace.ProjectId, ActorId = user.Id,
                    Action = form.RevisesId.HasValue ? "Resubmitted deliverable revision" : "Submitted deliverable",
                    CreatedAtUtc = DateTime.UtcNow });
                NotificationService.Queue(Db, workspace.Project.ClientId, "Có bản bàn giao mới cần nghiệm thu", "/Workspace/Index/" + workspace.Id);
                Db.SaveChanges();
                tx.Commit();
            }
            return RedirectToAction("Index", new { id = workspace.Id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Decide(int id, string decision, string feedback)
        {
            var user = CurrentUser;
            var deliverable = Db.Deliverables.Include(d => d.Workspace.Project).SingleOrDefault(d => d.Id == id);
            if (deliverable == null) return HttpNotFound();
            if (user == null || deliverable.Workspace.Project.ClientId != user.Id) return Forbidden();
            if (!CanWork(deliverable.Workspace) || deliverable.Status != "Submitted") return new HttpStatusCodeResult(409);
            if (decision != "Approved" && decision != "RevisionsRequested") return new HttpStatusCodeResult(400);
            feedback = (feedback ?? "").Trim();
            if (decision == "RevisionsRequested" && (!ModelState.IsValid || feedback.Length < 10 || feedback.Length > 1000))
            {
                TempData["Error"] = "Nêu rõ nội dung cần sửa từ 10 đến 1.000 ký tự để người thực hiện có thể cập nhật bản bàn giao.";
                return RedirectToAction("Index", new { id=deliverable.WorkspaceId });
            }
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var currentStatus = Db.Database.SqlQuery<string>(
                    "SELECT [Status] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0",
                    deliverable.Workspace.ProjectId).Single();
                Db.Entry(deliverable).Reload();
                if (!CanWork(deliverable.Workspace) || deliverable.Status != "Submitted") return new HttpStatusCodeResult(409);
                if (deliverable.EscrowMilestoneId.HasValue)
                {
                    var escrow = Db.EscrowMilestones.Single(m => m.Id == deliverable.EscrowMilestoneId.Value);
                    Db.Entry(escrow).Reload();
                    if (escrow.Status != "Submitted") return new HttpStatusCodeResult(409);
                    if (decision == "Approved") new WalletService(Db).ReleaseUnderLock(escrow.Id);
                    else { escrow.Status = "RevisionsRequested"; escrow.SubmittedAtUtc = null; }
                }
                deliverable.Status = decision;
                if (decision == "RevisionsRequested")
                    Db.WorkspaceMessages.Add(new WorkspaceMessage { WorkspaceId=deliverable.WorkspaceId, AuthorId=user.Id,
                        Body="Yêu cầu sửa bản bàn giao #" + id + " · " + deliverable.Title + ":\n" + feedback, CreatedAtUtc=DateTime.UtcNow });
                Db.ActivityLogs.Add(new ActivityLog { ProjectId = deliverable.Workspace.ProjectId, ActorId = user.Id,
                    Action = decision + " deliverable #" + deliverable.Id, CreatedAtUtc = DateTime.UtcNow });
                NotificationService.Queue(Db, deliverable.SubmittedById,
                    decision == "Approved" ? "Bản bàn giao của bạn đã được phê duyệt" : "Bản bàn giao cần sửa đổi",
                    "/Workspace/Index/" + deliverable.WorkspaceId);
                Db.SaveChanges();
                tx.Commit();
            }
            return RedirectToAction("Index", new { id = deliverable.WorkspaceId });
        }
    }
}
