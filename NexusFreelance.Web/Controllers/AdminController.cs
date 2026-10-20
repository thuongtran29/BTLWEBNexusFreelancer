using System;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public class AdminController : BaseController
    {
        private const int PageSize = 20;
        private AdminSkillCatalogService Catalog => new AdminSkillCatalogService(Db);

        [HttpGet]
        public ActionResult Badges(string q = "", string status = "Active", int page = 1)
        {
            q = (q ?? "").Trim();
            if (q.Length > 120 || !new[] { "Active", "Revoked", "All" }.Contains(status))
                return new HttpStatusCodeResult(400);
            var badges = Db.EarnedSkillBadges.AsNoTracking().AsQueryable();
            if (status == "Active") badges = badges.Where(b => !b.RevokedAtUtc.HasValue);
            if (status == "Revoked") badges = badges.Where(b => b.RevokedAtUtc.HasValue);
            if (q.Length > 0)
                badges = badges.Where(b => b.User.Email.Contains(q) || b.User.Name.Contains(q) ||
                    b.SkillTrack.Title.Contains(q) || b.SkillTrack.Code.Contains(q));
            var total = badges.Count();
            var pageCount = Math.Max(1, (total + PageSize - 1) / PageSize);
            page = Math.Max(1, Math.Min(page, pageCount));
            return View(new AdminBadgesPage
            {
                Query = q, Status = status, Total = total, Page = page, PageCount = pageCount,
                Badges = badges.Include(b => b.User).Include(b => b.SkillTrack)
                    .Include(b => b.EvidenceAttempt).Include(b => b.RevokedBy)
                    .OrderByDescending(b => b.Id).Skip((page - 1) * PageSize).Take(PageSize).ToList()
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult RevokeBadge(AdminRevokeBadgeForm form)
        {
            if (form == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(form.Reason))
                return new HttpStatusCodeResult(400);
            var reason = form.Reason.Trim();
            if (reason.Length < 5) return new HttpStatusCodeResult(400);

            var ownerId = Db.EarnedSkillBadges.AsNoTracking()
                .Where(b => b.Id == form.BadgeId).Select(b => (int?)b.UserAccountId).SingleOrDefault();
            if (!ownerId.HasValue) return HttpNotFound();
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                // Exams lock the user first. Use the same order to serialize revocations
                // with badge issuance and avoid an active badge from a racing submission.
                var owner = Db.Users.SqlQuery(
                    "SELECT * FROM dbo.Users WITH (UPDLOCK,HOLDLOCK) WHERE Id=@p0", ownerId.Value)
                    .SingleOrDefault();
                var badge = Db.EarnedSkillBadges.SqlQuery(
                    "SELECT * FROM dbo.EarnedSkillBadges WITH (UPDLOCK,HOLDLOCK) WHERE Id=@p0", form.BadgeId)
                    .SingleOrDefault();
                if (owner == null || badge == null || badge.UserAccountId != owner.Id)
                    return HttpNotFound();
                if (owner.Role != "Freelancer" || badge.RevokedAtUtc.HasValue)
                    return new HttpStatusCodeResult(HttpStatusCode.Conflict);
                badge.RevokedAtUtc = DateTime.UtcNow;
                badge.RevokedById = CurrentUser.Id;
                badge.RevocationReason = reason;
                Db.AdminAuditLogs.Add(new AdminAuditLog
                {
                    ActorId = CurrentUser.Id, TargetUserId = owner.Id,
                    Action = "RevokeBadge", Reason = "Badge #" + badge.Id + ": " + reason,
                    CreatedAtUtc = DateTime.UtcNow
                });
                NotificationService.Queue(Db, owner.Id,
                    "Một Badge nội bộ đã bị thu hồi. Xem lại trung tâm kỹ năng để thi lại.",
                    "/Skills/Index");
                Db.SaveChanges();
                tx.Commit();
            }
            TempData["Notice"] = "Đã thu hồi Badge, ghi audit và gửi thông báo cho Freelancer.";
            return RedirectToAction("Badges");
        }

        [HttpGet]
        public ActionResult Reports(string status = "Pending", int page = 1)
        {
            if (!new[] { "Pending", "Confirmed", "Dismissed", "All" }.Contains(status))
                return new HttpStatusCodeResult(400);
            IQueryable<ProjectAbuseReport> query = Db.ProjectAbuseReports.AsNoTracking();
            if (status != "All") query = query.Where(r => r.Status == status);
            var count = query.Count();
            var pages = Math.Max(1, (count + PageSize - 1) / PageSize);
            page = Math.Max(1, Math.Min(page, pages));
            ViewBag.Status = status;
            ViewBag.Page = page;
            ViewBag.PageCount = pages;
            ViewBag.Total = count;
            return View(query.Include(r => r.Project).Include(r => r.Reporter)
                .Include(r => r.Reviewer).OrderByDescending(r => r.Id)
                .Skip((page - 1) * PageSize).Take(PageSize).ToList());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult ReviewReport(AdminReviewReportForm form)
        {
            if (form == null || !ModelState.IsValid ||
                (form.Decision != "Confirmed" && form.Decision != "Dismissed"))
                return new HttpStatusCodeResult(400);
            var explanation = (form.Resolution ?? "").Trim();
            if (explanation.Length < 5 || explanation.Length > 200) return new HttpStatusCodeResult(400);
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                var report = Db.ProjectAbuseReports.SqlQuery(
                    "SELECT * FROM dbo.ProjectAbuseReports WITH (UPDLOCK,HOLDLOCK) WHERE Id=@p0", form.Id)
                    .SingleOrDefault();
                if (report == null) return HttpNotFound();
                if (report.Status != "Pending") return new HttpStatusCodeResult(HttpStatusCode.Conflict);
                var ownerId = Db.Projects.Where(p => p.Id == report.ProjectId)
                    .Select(p => p.ClientId).Single();
                report.Status = form.Decision;
                report.Resolution = explanation;
                report.ReviewerId = CurrentUser.Id;
                report.ReviewedAtUtc = DateTime.UtcNow;
                var auditReason = "Report #" + report.Id + " / Project #" + report.ProjectId + " / " + form.Decision + ": " + explanation;
                Db.AdminAuditLogs.Add(new AdminAuditLog
                {
                    ActorId = CurrentUser.Id, TargetUserId = ownerId,
                    Action = "ReviewProjectReport", Reason = auditReason.Substring(0, Math.Min(200, auditReason.Length)),
                    CreatedAtUtc = DateTime.UtcNow
                });
                NotificationService.Queue(Db, report.ReporterId,
                    form.Decision == "Confirmed" ? "Báo cáo dự án đã được xác nhận." :
                    "Báo cáo dự án đã được xem xét.", "/Reports/Mine");
                Db.SaveChanges();
                tx.Commit();
            }
            TempData["Notice"] = "Đã ghi quyết định xử lý và nhật ký kiểm duyệt.";
            return RedirectToAction("Reports");
        }

        [HttpGet]
        public ActionResult Certifications(string status = "Pending", int page = 1)
        {
            var statuses = new[] { "Pending", "Verified", "Rejected", "All" };
            if (!statuses.Contains(status)) return new HttpStatusCodeResult(400);
            ViewBag.TrackOptions = Db.SkillTracks.Where(t => t.IsActive != false).OrderBy(t => t.Title).Take(200).ToList();
            IQueryable<ExternalCertification> query = Db.ExternalCertifications.AsNoTracking();
            if (status != "All") query = query.Where(c => c.Status == status);
            var total = query.Count();
            var pageCount = Math.Max(1, (total + PageSize - 1) / PageSize);
            page = Math.Max(1, Math.Min(page, pageCount));
            return View(new CertificationAdminPage
            {
                Status = status, Page = page, PageCount = pageCount, Total = total,
                Certifications = query.Include(c => c.User).Include(c => c.ReviewedBy)
                    .OrderByDescending(c => c.Id).Skip((page - 1) * PageSize)
                    .Take(PageSize).ToList()
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult ClassifyCertification(int id, int skillId, int level, string reason)
        {
            if (!ModelState.IsValid) return new HttpStatusCodeResult(400);
            try { new CertificationService(Db).Classify(CurrentUser.Id, id, skillId, level, reason); TempData["Notice"] = "Đã cập nhật phân loại kỹ năng của chứng chỉ."; }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            return RedirectToAction("Certifications", new { status = "Verified" });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult ReviewCertification(CertificationReviewForm form)
        {
            if (form == null || !ModelState.IsValid) return new HttpStatusCodeResult(400);
            try
            {
                if (!new CertificationService(Db).Review(CurrentUser.Id, form))
                    return new HttpStatusCodeResult(HttpStatusCode.Conflict,
                        "Chứng chỉ đã được xử lý hoặc không tồn tại.");
            }
            catch (InvalidOperationException ex) { return new HttpStatusCodeResult(400, ex.Message); }
            TempData["Notice"] = "Đã lưu kết quả kiểm duyệt chứng chỉ.";
            return RedirectToAction("Certifications", new { status = "Pending" });
        }

        protected override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            if (context.Result != null) return;
            if (CurrentUser == null || CurrentUser.Role != "Admin")
            {
                context.Result = Forbidden();
                return;
            }
        }

        [HttpGet]
        public ActionResult Index()
        {
            return View(new AdminDashboardPage
            {
                UserCount = Db.Users.Count(),
                SuspendedCount = Db.Users.Count(u => u.IsSuspended),
                ProjectCount = Db.Projects.Count(),
                AuditCount = Db.AdminAuditLogs.Count()
            });
        }

        [HttpGet]
        public ActionResult Users(string q, int page = 1)
        {
            var query = (q ?? "").Trim();
            if (query.Length > 120) return new HttpStatusCodeResult(400);
            page = Math.Max(1, page);
            IQueryable<UserAccount> users = Db.Users.AsNoTracking();
            if (query.Length > 0)
                users = users.Where(u => u.Name.Contains(query) || u.Email.Contains(query));
            var total = users.Count();
            var pageCount = Math.Max(1, (total + PageSize - 1) / PageSize);
            page = Math.Min(page, pageCount);
            return View(new AdminUsersPage
            {
                Query = query,
                Page = page,
                PageCount = pageCount,
                Total = total,
                Users = users.OrderBy(u => u.Id).Skip((page - 1) * PageSize).Take(PageSize)
                    .Select(u => new AdminUserRow
                    {
                        Id = u.Id, Name = u.Name, Email = u.Email,
                        Role = u.Role, IsSuspended = u.IsSuspended, JoinedAtUtc = u.JoinedAtUtc
                    }).ToList()
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult ChangeStatus(AdminChangeStatusForm form)
        {
            if (form == null || !ModelState.IsValid) return new HttpStatusCodeResult(400);
            form.Reason = form.Reason.Trim();
            if (form.Reason.Length < 5) return new HttpStatusCodeResult(400);
            if (form.Id == CurrentUser.Id) return Forbidden();

            using (var tx = Db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                // The WHERE predicate protects administrator accounts and makes concurrent
                // repeated requests update at most once; audit and update share a transaction.
                var changed = Db.Database.ExecuteSqlCommand(
                    "UPDATE dbo.Users SET IsSuspended = @p0 WHERE Id = @p1 " +
                    "AND Role IN ('Client', 'Freelancer') AND IsSuspended <> @p0",
                    form.Suspend, form.Id);
                if (changed != 1)
                {
                    tx.Rollback();
                    return new HttpStatusCodeResult(HttpStatusCode.Conflict);
                }
                Db.AdminAuditLogs.Add(new AdminAuditLog
                {
                    ActorId = CurrentUser.Id,
                    TargetUserId = form.Id,
                    Action = form.Suspend ? "SuspendUser" : "RestoreUser",
                    Reason = form.Reason,
                    CreatedAtUtc = DateTime.UtcNow
                });
                Db.SaveChanges();
                tx.Commit();
            }
            TempData["Notice"] = form.Suspend ? "Đã khóa tài khoản." : "Đã mở khóa tài khoản.";
            return RedirectToAction("Users");
        }

        [HttpGet]
        public ActionResult Audit(int page = 1, string q = "", string actionFilter = "", string from = "", string to = "", int? userId = null)
        {
            q = (q ?? "").Trim(); actionFilter = (actionFilter ?? "").Trim();
            DateTime? start, end;
            if (q.Length > 120 || actionFilter.Length > 40 || userId <= 0 || !ActivityFilters.TryDates(from, to, out start, out end))
                return new HttpStatusCodeResult(400);
            var query = Db.AdminAuditLogs.AsNoTracking().AsQueryable();
            if (q.Length > 0) query = query.Where(x => x.Actor.Name.Contains(q) || x.Actor.Email.Contains(q) ||
                x.TargetUser.Name.Contains(q) || x.TargetUser.Email.Contains(q) || x.Reason.Contains(q));
            if (actionFilter.Length > 0) query = query.Where(x => x.Action == actionFilter);
            if (userId.HasValue) query = query.Where(x => x.ActorId == userId.Value || x.TargetUserId == userId.Value);
            if (start.HasValue) query = query.Where(x => x.CreatedAtUtc >= start.Value);
            if (end.HasValue) query = query.Where(x => x.CreatedAtUtc < end.Value);
            var total = query.Count();
            var pageCount = Math.Max(1, (total + PageSize - 1) / PageSize);
            page = Math.Max(1, Math.Min(page, pageCount));
            var logs = query
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Skip((page - 1) * PageSize).Take(PageSize)
                .Select(x => new AdminAuditRow
                {
                    ActorId = x.ActorId, TargetUserId = x.TargetUserId,
                    Id = x.Id, Actor = x.Actor.Email, Target = x.TargetUser.Email,
                    Action = x.Action, Reason = x.Reason, CreatedAtUtc = x.CreatedAtUtc
                }).ToList();
            return View(new AdminAuditPage { Query = q, Action = actionFilter, From = from, To = to, UserId = userId,
                Total = total, Page = page, PageCount = pageCount, Logs = logs });
        }

        [HttpGet]
        public ActionResult AuditDetails(int id)
        {
            var row = Db.AdminAuditLogs.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new AdminAuditRow { Id = x.Id, ActorId = x.ActorId, TargetUserId = x.TargetUserId,
                    Actor = x.Actor.Name + " (" + x.Actor.Email + ")", Target = x.TargetUser.Name + " (" + x.TargetUser.Email + ")",
                    Action = x.Action, Reason = x.Reason, CreatedAtUtc = x.CreatedAtUtc }).SingleOrDefault();
            return row == null ? (ActionResult)HttpNotFound() : View(row);
        }

        [HttpGet]
        public ActionResult UserProfile(int id)
        {
            var page = new ActivityInspectionService(Db).Profile(id);
            return page == null ? (ActionResult)HttpNotFound() : View(page);
        }

        [HttpGet]
        public ActionResult Activity(string q = "", string controllerFilter = "", string actionFilter = "", string from = "", string to = "", int? userId = null, int page = 1)
        {
            q = (q ?? "").Trim(); controllerFilter = (controllerFilter ?? "").Trim(); actionFilter = (actionFilter ?? "").Trim();
            DateTime? start, end;
            if (q.Length > 120 || controllerFilter.Length > 60 || actionFilter.Length > 60 || userId <= 0 ||
                !ActivityFilters.TryDates(from, to, out start, out end)) return new HttpStatusCodeResult(400);
            // Retention is enforced in reads too, even when no new page sample triggers batch cleanup.
            var cutoff = DateTime.UtcNow.AddDays(-30);
            var query = Db.Set<UserAccessLog>().AsNoTracking().Where(x => x.AccessedAtUtc >= cutoff);
            if (q.Length > 0) query = query.Where(x => x.User.Name.Contains(q) || x.User.Email.Contains(q));
            if (controllerFilter.Length > 0) query = query.Where(x => x.Controller == controllerFilter);
            if (actionFilter.Length > 0) query = query.Where(x => x.Action == actionFilter);
            if (userId.HasValue) query = query.Where(x => x.UserAccountId == userId.Value);
            if (start.HasValue) query = query.Where(x => x.AccessedAtUtc >= start.Value);
            if (end.HasValue) query = query.Where(x => x.AccessedAtUtc < end.Value);
            var total = query.Count();
            var pages = Math.Max(1, (total + PageSize - 1) / PageSize);
            page = Math.Max(1, Math.Min(page, pages));
            return View(new AdminAccessPage { Query = q, Controller = controllerFilter, Action = actionFilter, From = from, To = to,
                UserId = userId, Total = total, Page = page, PageCount = pages,
                Logs = query.OrderByDescending(x => x.AccessedAtUtc).ThenByDescending(x => x.Id)
                    .Skip((page - 1) * PageSize).Take(PageSize).Select(x => new AdminAccessRow {
                        Id = x.Id, UserAccountId = x.UserAccountId, Name = x.User.Name, Email = x.User.Email,
                        Controller = x.Controller, Action = x.Action, AccessedAtUtc = x.AccessedAtUtc }).ToList() });
        }

        [HttpGet]
        public ActionResult Skills()
        {
            return View(new AdminSkillCatalogPage
            {
                Tracks = Db.SkillTracks.AsNoTracking().OrderBy(t => t.Code).ToList()
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateSkill(AdminCreateSkillForm form)
        {
            if (form == null || !ModelState.IsValid ||
                string.IsNullOrWhiteSpace(form.Title) ||
                string.IsNullOrWhiteSpace(form.Description) ||
                string.IsNullOrWhiteSpace(form.MatchKeywords) || !IndustryCatalog.IsValid(form.Industry)) return new HttpStatusCodeResult(400);
            try { Catalog.CreateTrack(CurrentUser.Id, form); }
            catch (InvalidOperationException) { return new HttpStatusCodeResult(409, "Mã kỹ năng đã tồn tại."); }
            TempData["Notice"] = "Đã tạo danh mục kỹ năng.";
            return RedirectToAction("Skills");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult UpdateSkill(AdminUpdateSkillForm form)
        {
            if (form == null || !ModelState.IsValid ||
                string.IsNullOrWhiteSpace(form.Title) ||
                string.IsNullOrWhiteSpace(form.Description) ||
                string.IsNullOrWhiteSpace(form.MatchKeywords) || !IndustryCatalog.IsValid(form.Industry)) return new HttpStatusCodeResult(400);
            if (!Catalog.UpdateTrack(CurrentUser.Id, form)) return HttpNotFound();
            TempData["Notice"] = "Đã cập nhật kỹ năng.";
            return RedirectToAction("Skills");
        }

        [HttpGet]
        public ActionResult Questions(int? id = null)
        {
            if (!id.HasValue)
            {
                ViewBag.QuestionCounts = Db.SkillExamQuestions.AsNoTracking().GroupBy(q => q.SkillTrackId)
                    .Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionary(g => g.Id, g => g.Count);
                return View("QuestionCatalog", new AdminSkillCatalogPage
                {
                    Tracks = Db.SkillTracks.AsNoTracking().Where(t => t.IsActive != false)
                        .OrderBy(t => t.Industry).ThenBy(t => t.Title).ToList()
                });
            }
            var track = Db.SkillTracks.AsNoTracking().SingleOrDefault(t => t.Id == id.Value);
            if (track == null) return HttpNotFound();
            var questions = Db.SkillExamQuestions.AsNoTracking().Where(q => q.SkillTrackId == id.Value)
                .OrderBy(q => q.Ordinal).ToList();
            var ids = questions.Select(q => q.Id).ToList();
            var usedIds = Db.SkillExamAnswers.AsNoTracking()
                .Where(a => ids.Contains(a.SkillExamQuestionId))
                .Select(a => a.SkillExamQuestionId).Distinct().ToList();
            return View(new AdminQuestionBankPage
            {
                Track = track, Questions = questions,
                LockedQuestionIds = new System.Collections.Generic.HashSet<int>(usedIds)
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult SaveQuestion(AdminSaveQuestionForm form)
        {
            if (form == null || !ModelState.IsValid ||
                new[] { form.Prompt, form.ChoiceA, form.ChoiceB, form.ChoiceC, form.ChoiceD }
                    .Any(string.IsNullOrWhiteSpace)) return new HttpStatusCodeResult(400);
            try
            {
                if (!Catalog.SaveQuestion(CurrentUser.Id, form)) return HttpNotFound();
            }
            catch (InvalidOperationException ex)
            {
                return new HttpStatusCodeResult(409, ex.Message);
            }
            TempData["Notice"] = form.Id == 0 ? "Đã thêm câu hỏi." : "Đã cập nhật câu hỏi.";
            return RedirectToAction("Questions", new { id = form.SkillTrackId });
        }
    }
}
