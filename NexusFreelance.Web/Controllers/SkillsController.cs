using System;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public class SkillsController : BaseController
    {
        private SkillVerificationService Exams => new SkillVerificationService(Db);

        private MyCertificationsPage LoadCertifications(SubmitCertificationForm form = null)
        {
            ViewBag.TrackOptions = Db.SkillTracks.Where(t => t.IsActive != false).OrderBy(t => t.Title).Take(200).ToList();
            return new MyCertificationsPage
            {
                NewCertificate = form ?? new SubmitCertificationForm(),
                Certifications = Db.ExternalCertifications.AsNoTracking()
                    .Where(c => c.UserAccountId == CurrentUser.Id)
                    .OrderByDescending(c => c.Id).ToList()
            };
        }

        [HttpGet]
        public ActionResult Certifications(string resource)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            var item = AssessmentCatalog.Find(resource);
            if (resource != null && (item == null || item.CredentialName == null)) return HttpNotFound();
            return View(LoadCertifications(item == null ? null : new SubmitCertificationForm { Name = item.CredentialName, Issuer = item.Provider }));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult SubmitCertification(SubmitCertificationForm form)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            if (form == null) return new HttpStatusCodeResult(400);
            if (!ModelState.IsValid) return View("Certifications", LoadCertifications(form));
            try
            {
                new CertificationService(Db).Submit(CurrentUser.Id, form);
                TempData["Notice"] = "Đã gửi chứng chỉ. Quản trị viên sẽ kiểm tra minh chứng.";
                return RedirectToAction("Certifications");
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View("Certifications", LoadCertifications(form));
            }
        }

        [HttpGet]
        public ActionResult Index()
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            return View(Exams.LoadCenter(CurrentUser.Id));
        }

        [HttpGet]
        public ActionResult Exam(int id)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            var page = Exams.LoadExam(id, CurrentUser.Id);
            if (page == null) return HttpNotFound();
            return View(page);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Submit(int id, ExamSubmissionForm form)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            if (!ModelState.IsValid || form == null || form.SkillTrackId != id)
                return new HttpStatusCodeResult(400, "Dữ liệu bài thi không hợp lệ.");
            try
            {
                var attempt = Exams.Submit(CurrentUser.Id, id, form);
                return RedirectToAction("Result", new { id = attempt.Id });
            }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            catch (System.Data.Entity.Infrastructure.DbUpdateException) { TempData["Error"] = "Không thể lưu kết quả. Hãy thử lại."; }
            catch (System.Data.SqlClient.SqlException) { TempData["Error"] = "Không thể xử lý bài thi lúc này. Hãy thử lại."; }
            return RedirectToAction("Exam", new { id });
        }

        [HttpGet]
        public ActionResult Result(int id)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            var attempt = Exams.FindOwnAttempt(id, CurrentUser.Id);
            if (attempt == null) return HttpNotFound();
            return View(attempt);
        }
    }
}
