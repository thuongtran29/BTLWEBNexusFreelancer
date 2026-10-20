using System;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public class ReportsController : BaseController
    {
        [HttpGet]
        public ActionResult Mine()
        {
            var user = CurrentUser;
            if (user == null || (user.Role != "Client" && user.Role != "Freelancer")) return Forbidden();
            return View(Db.ProjectAbuseReports.AsNoTracking().Include(r => r.Project)
                .Where(r => r.ReporterId == user.Id).OrderByDescending(r => r.Id).Take(100).ToList());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Submit(ProjectAbuseReportForm form)
        {
            var user = CurrentUser;
            if (user == null || (user.Role != "Client" && user.Role != "Freelancer")) return Forbidden();
            if (form == null || !ModelState.IsValid ||
                !new[] { "Fraud", "Spam", "Abuse", "Other" }.Contains(form.Category))
                return new HttpStatusCodeResult(400);
            var details = (form.Details ?? "").Trim();
            if (details.Length < 15 || details.Length > 2000) return new HttpStatusCodeResult(400);
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                // Project-level lock also serializes duplicate submissions from one reporter.
                var project = Db.Projects.SqlQuery(
                    "SELECT * FROM dbo.Projects WITH (UPDLOCK,HOLDLOCK) WHERE Id=@p0", form.ProjectId)
                    .SingleOrDefault();
                if (project == null) return HttpNotFound();
                if (project.ClientId == user.Id) return Forbidden();
                if (project.Status != "Published" && project.Status != "InProgress" &&
                    project.Status != "Completed") return new HttpStatusCodeResult(HttpStatusCode.Conflict);
                if (Db.ProjectAbuseReports.Any(r => r.ProjectId == project.Id &&
                    r.ReporterId == user.Id && r.Status == "Pending"))
                    return new HttpStatusCodeResult(HttpStatusCode.Conflict);
                Db.ProjectAbuseReports.Add(new ProjectAbuseReport
                {
                    ProjectId = project.Id, ReporterId = user.Id, Category = form.Category,
                    Details = details, Status = "Pending", CreatedAtUtc = DateTime.UtcNow
                });
                Services.NotificationService.QueueAdmins(Db, "Phản ánh mới từ " + user.Name + " về dự án #" + project.Id,
                    "/Admin/Reports", user.Id, project.Id);
                Db.SaveChanges();
                tx.Commit();
            }
            TempData["Notice"] = "Đã gửi báo cáo để quản trị viên xem xét.";
            return RedirectToAction("Mine");
        }
    }
}
