using System;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Data.Entity;
using System.Web.Mvc;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Controllers
{
    public class ClientProfileForm
    {
        [Required, RegularExpression("Individual|Business")] public string ClientType { get; set; }
        [StringLength(180)] public string CompanyName { get; set; }
        [StringLength(120)] public string Location { get; set; }
        [StringLength(1000)] public string About { get; set; }
        [StringLength(1000), Url] public string Website { get; set; }
    }
    [Authorize]
    public class ClientProfileController : BaseController
    {
        [HttpGet]
        public ActionResult Index()
        {
            if (CurrentUser.Role != "Client") return Forbidden();
            LoadReviews();
            var p = Db.ClientProfiles.SingleOrDefault(c => c.UserAccountId == CurrentUser.Id);
            return View(new ClientProfileForm { ClientType = p?.ClientType ?? "Individual", CompanyName = p?.CompanyName, Location = p?.Location, About = p?.About, Website = p?.Website });
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Index(ClientProfileForm form)
        {
            if (CurrentUser.Role != "Client") return Forbidden();
            if (form == null) return new HttpStatusCodeResult(400);
            if (form.ClientType == "Business" && string.IsNullOrWhiteSpace(form.CompanyName)) ModelState.AddModelError("CompanyName", "Khách hàng doanh nghiệp cần tên công ty.");
            Uri website;
            if (!string.IsNullOrEmpty(form.Website) && (!Uri.TryCreate(form.Website, UriKind.Absolute, out website) || website.Scheme != "https")) ModelState.AddModelError("Website", "Website phải dùng HTTPS.");
            if (!ModelState.IsValid) { LoadReviews(); return View(form); }
            var p = Db.ClientProfiles.SingleOrDefault(c => c.UserAccountId == CurrentUser.Id);
            if (p == null) { p = new ClientProfile { UserAccountId = CurrentUser.Id }; Db.ClientProfiles.Add(p); }
            p.ClientType = form.ClientType; p.CompanyName = form.ClientType == "Business" ? form.CompanyName.Trim() : null;
            p.Location = form.Location?.Trim(); p.About = form.About?.Trim(); p.Website = form.Website?.Trim(); p.UpdatedAtUtc = DateTime.UtcNow;
            Db.SaveChanges(); TempData["Notice"] = "Đã lưu hồ sơ khách hàng."; return RedirectToAction("Index");
        }
        private void LoadReviews()
        {
            ViewBag.Reviews = Db.ClientReviews.Include(r => r.Freelancer).Include(r => r.Project)
                .Where(r => r.ClientId == CurrentUser.Id).OrderByDescending(r => r.Id).Take(30).ToList();
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Review(int projectId, int rating, string comment)
        {
            if (CurrentUser.Role != "Freelancer") return Forbidden();
            if (rating < 1 || rating > 5 || string.IsNullOrWhiteSpace(comment) || comment.Length > 1000) return new HttpStatusCodeResult(400);
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var p = Db.Projects.SqlQuery("SELECT * FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", projectId).SingleOrDefault();
                if (p == null) return HttpNotFound();
                if (p.Status != "Completed" || !Db.RoleAssignments.Any(r => r.ProjectId == projectId && r.FreelancerId == CurrentUser.Id)) return Forbidden();
                if (Db.ClientReviews.Any(r => r.ProjectId == projectId && r.FreelancerId == CurrentUser.Id)) return new HttpStatusCodeResult(409);
                Db.ClientReviews.Add(new ClientReview { ProjectId = projectId, ClientId = p.ClientId, FreelancerId = CurrentUser.Id,
                    Rating = rating, Comment = comment.Trim(), CreatedAtUtc = DateTime.UtcNow });
                Db.SaveChanges(); tx.Commit();
            }
            TempData["Notice"] = "Đã gửi đánh giá khách hàng."; return RedirectToAction("Project", "Wallet", new { id = projectId });
        }
    }
}
