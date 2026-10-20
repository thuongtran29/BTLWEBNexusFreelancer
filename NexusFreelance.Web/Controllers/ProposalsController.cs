using System;
using System.Data;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public class ProposalsController : BaseController
    {
        [HttpGet]
        public ActionResult Mine()
        {
            var user = CurrentUser;
            if (user?.Role != "Freelancer") return Forbidden();
            ViewBag.Scores = Db.MatchingResults.Where(m => m.Proposal.ApplicantId == user.Id).ToList()
                .ToDictionary(m => m.ProposalId);
            return View(Db.Proposals.Include("Project").Include("Requirement").Include("GroupBid")
                .Where(p => p.ApplicantId == user.Id).OrderByDescending(p => p.CreatedAtUtc).ToList());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Apply(ProposalForm form)
        {
            var user = CurrentUser;
            if (user?.Role != "Freelancer") return Forbidden();
            if (form == null || form.ProjectId <= 0 || form.RoleRequirementId <= 0)
                return new HttpStatusCodeResult(400, "Thiếu thông tin ứng tuyển.");
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(form.Pitch))
            {
                TempData["Error"] = "Nội dung đề xuất hoặc giá báo không hợp lệ.";
                return RedirectToAction("Details", "Projects", new { id = form.ProjectId });
            }
            try
            {
                PaymentPlan.Parse(form.MilestonePlan, form.QuotedPrice, form.ProjectId,form.DeliveryDays);
                using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    // The same project lock is taken by AcceptProposal and LockTeam.
                    // Read every mutable eligibility condition after acquiring it.
                    var lockedProjectId = Db.Database.SqlQuery<int>(
                        "SELECT [Id] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0",
                        form.ProjectId).SingleOrDefault();
                    if (lockedProjectId == 0) return HttpNotFound();
                    var project = Db.Projects.Single(p => p.Id == form.ProjectId);
                    if (project.Status != "Published") return new HttpStatusCodeResult(409);
                    if (!RecruitmentPolicy.DateAllowsApplications(project.DeadlineUtc, DateTime.UtcNow))
                        return new HttpStatusCodeResult(409, "Dự án đã quá hạn dự kiến. Khách hàng cần cập nhật hạn trước khi nhận đề xuất mới.");
                    if (project.Client.Email == user.Email || (!string.IsNullOrEmpty(user.GoogleSubject) && user.GoogleSubject == project.Client.GoogleSubject))
                        return new HttpStatusCodeResult(409, "Hai hồ sơ cùng người không thể tự ứng tuyển hoặc giao dịch với nhau.");
                    if (project.ProjectType == "TEAM") return new HttpStatusCodeResult(409, "Công việc nhóm cần chào giá từ nhóm, do trưởng nhóm gửi.");
                    if (!WalletService.ValidAmount(form.QuotedPrice)) return new HttpStatusCodeResult(400, "Giá chào phải là số nguyên VND lớn hơn 0.");

                    var lockedRoleId = Db.Database.SqlQuery<int>(
                        "SELECT [Id] FROM [dbo].[RoleRequirements] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0",
                        form.RoleRequirementId).SingleOrDefault();
                    if (lockedRoleId == 0) return new HttpStatusCodeResult(400);
                    var role = Db.RoleRequirements.Single(r => r.Id == form.RoleRequirementId);
                    if (role.ProjectId != project.Id) return new HttpStatusCodeResult(400);
                    if (Db.Proposals.Any(p => p.RoleRequirementId == role.Id && p.ApplicantId == user.Id))
                    {
                        TempData["Error"] = "Bạn đã ứng tuyển vị trí này.";
                        return RedirectToAction("Details", "Projects", new { id = project.Id });
                    }
                    if (Db.RoleAssignments.Count(a => a.RoleRequirementId == role.Id) >= role.RequiredCount)
                    {
                        TempData["Error"] = "Vị trí đã tuyển đủ.";
                        return RedirectToAction("Details", "Projects", new { id = project.Id });
                    }
                    // Create both records inside a single transaction; do not publish a proposal without its snapshot.
                    var proposal = new Proposal { ProjectId = project.Id, RoleRequirementId = role.Id, ApplicantId = user.Id,
                        MilestonePlan = form.MilestonePlan, DeliveryDays = form.DeliveryDays, Pitch = form.Pitch.Trim(), QuotedPrice = form.QuotedPrice, Status = "Pending", CreatedAtUtc = DateTime.UtcNow };
                    Db.Proposals.Add(proposal);
                    Db.SaveChanges();
                    var snapshot = new MatchingService(Db).CreateSnapshot(user.Id, project, role, form.QuotedPrice);
                    snapshot.ProposalId = proposal.Id;
                    Db.MatchingResults.Add(snapshot);
                    NotificationService.Queue(Db, project.ClientId, "Có ứng viên mới cho dự án: " + project.Title.Substring(0, Math.Min(project.Title.Length, 120)), "/Projects/Manage/" + project.Id);
                    Db.SaveChanges();
                    tx.Commit();
                }
                TempData["Notice"] = "Đã gửi Proposal và lưu điểm Matching tại thời điểm nộp.";
            }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            catch (System.Data.Entity.Infrastructure.DbUpdateException) { TempData["Error"] = "Proposal đã tồn tại hoặc dữ liệu không hợp lệ."; }
            catch (System.Data.SqlClient.SqlException) { TempData["Error"] = "Không thể lưu Proposal lúc này. Hãy tải lại và thử lại."; }
            return RedirectToAction("Details", "Projects", new { id = form.ProjectId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Withdraw(int id)
        {
            var user = CurrentUser;
            if (user?.Role != "Freelancer") return Forbidden();
            // Resolve the immutable parent id without holding a transaction lock.
            // Inside the transaction all proposal mutations lock Project first,
            // then Proposal, preventing a lock-order deadlock with AcceptProposal.
            var projectId = Db.Proposals.Where(p => p.Id == id && p.ApplicantId == user.Id)
                .Select(p => (int?)p.ProjectId).SingleOrDefault();
            if (!projectId.HasValue) return HttpNotFound();
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                // Serialize against client Accept and project lifecycle transitions.
                Db.Database.SqlQuery<int>(
                    "SELECT [Id] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0",
                    projectId.Value).SingleOrDefault();
                Db.Database.SqlQuery<int>(
                    "SELECT [Id] FROM [dbo].[Proposals] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", id).SingleOrDefault();
                var proposal = Db.Proposals.SingleOrDefault(p => p.Id == id && p.ApplicantId == user.Id && p.ProjectId == projectId.Value);
                if (proposal == null) return HttpNotFound();
                if (proposal.GroupBidId.HasValue)
                    return new HttpStatusCodeResult(409, "Chỉ Leader nhóm được rút Proposal nhóm.");
                if ((proposal.Status != "Pending" && proposal.Status != "Shortlisted") || Db.RoleAssignments.Any(a => a.ProposalId == id))
                    return new HttpStatusCodeResult(409, "Chỉ có thể rút đề xuất đang chờ xét.");
                proposal.Status = "Withdrawn";
                Db.ActivityLogs.Add(new ActivityLog { ProjectId = projectId.Value, ActorId = user.Id,
                    Action = "Withdrew proposal #" + id, CreatedAtUtc = DateTime.UtcNow });
                var clientId = Db.Projects.Where(p => p.Id == projectId.Value).Select(p => p.ClientId).Single();
                NotificationService.Queue(Db, clientId, "Một ứng viên đã rút Proposal", "/Projects/Manage/" + projectId.Value);
                Db.SaveChanges();
                tx.Commit();
            }
            TempData["Notice"] = "Đã rút đề xuất. Lịch sử ứng tuyển vẫn được lưu để đối chiếu.";
            return RedirectToAction("Mine");
        }
    }
}
