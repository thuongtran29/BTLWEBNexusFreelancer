using System;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public class ProfileController : BaseController
    {
        private ProfileService Profiles => new ProfileService(Db);

        private bool ValidPortfolioProject(PortfolioItemForm form) => !form.ProjectId.HasValue ||
            Db.RoleAssignments.Any(a => a.ProjectId == form.ProjectId.Value && a.FreelancerId == CurrentUser.Id && a.Project.Status == "Completed");

        private static bool IsHttps(string link)
        {
            Uri parsed;
            return Uri.TryCreate(link, UriKind.Absolute, out parsed) && parsed.Scheme == Uri.UriSchemeHttps;
        }

        [HttpGet]
        public ActionResult Index()
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            ViewBag.CurrentUserId = CurrentUser.Id;
            return View(Profiles.Load(CurrentUser.Id));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Update(FreelancerProfileForm form)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            ViewBag.CurrentUserId = CurrentUser.Id;
            if (form == null)
            {
                ModelState.AddModelError("", "Dữ liệu hồ sơ không hợp lệ.");
                form = new FreelancerProfileForm();
            }
            if (string.IsNullOrWhiteSpace(form.CoreRole))
                ModelState.AddModelError("CoreRole", "Vui lòng nhập chức danh nghề nghiệp.");
            if (!ModelState.IsValid)
            {
                var page = Profiles.Load(CurrentUser.Id);
                page.Profile = form;
                return View("Index", page);
            }
            try { Profiles.SaveProfile(CurrentUser.Id, form); TempData["Notice"] = "Đã cập nhật hồ sơ."; }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                var page = Profiles.Load(CurrentUser.Id);
                page.Profile = form;
                return View("Index", page);
            }
            catch (System.Data.Entity.Infrastructure.DbUpdateException) { TempData["Error"] = "Không lưu được hồ sơ. Hãy thử lại."; }
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult AddPortfolio(PortfolioItemForm form)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            if (form == null)
            { TempData["Error"] = "Dữ liệu Portfolio không hợp lệ."; return RedirectToAction("Index"); }
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(form.Title) || string.IsNullOrWhiteSpace(form.Summary) || !IsHttps(form.Url) || !ValidPortfolioProject(form))
            { TempData["Error"] = "Portfolio cần tiêu đề, mô tả và URL HTTPS hợp lệ."; return RedirectToAction("Index"); }
            Profiles.AddPortfolio(CurrentUser.Id, form);
            TempData["Notice"] = "Đã thêm dự án vào Portfolio (thông tin tự khai báo).";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public ActionResult EditPortfolio(int id)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            var item = Profiles.FindOwnedPortfolio(id, CurrentUser.Id);
            if (item == null) return HttpNotFound();
            ViewBag.ItemId = id;
            ViewBag.CompletedProjects = Profiles.CompletedProjects(CurrentUser.Id);
            return View(new PortfolioItemForm { Title = item.Title, Summary = item.Summary, Url = item.Url, Role = item.Role, Skills = item.Skills, Outcome = item.Outcome, ProjectId = item.ProjectId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditPortfolio(int id, PortfolioItemForm form)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            if (form == null) return new HttpStatusCodeResult(400, "Dữ liệu Portfolio không hợp lệ.");
            var item = Profiles.FindOwnedPortfolio(id, CurrentUser.Id);
            if (item == null) return HttpNotFound();
            ViewBag.ItemId = id;
            ViewBag.CompletedProjects = Profiles.CompletedProjects(CurrentUser.Id);
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(form.Title) || string.IsNullOrWhiteSpace(form.Summary) || !IsHttps(form.Url) || !ValidPortfolioProject(form))
            { ModelState.AddModelError("", "Vui lòng nhập đầy đủ thông tin Portfolio với URL HTTPS hợp lệ."); return View(form); }
            Profiles.UpdatePortfolio(item, form);
            TempData["Notice"] = "Đã sửa Portfolio.";
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult DeletePortfolio(int id)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            var item = Profiles.FindOwnedPortfolio(id, CurrentUser.Id);
            if (item == null) return HttpNotFound();
            Profiles.DeletePortfolio(item);
            TempData["Notice"] = "Đã xóa Portfolio.";
            return RedirectToAction("Index");
        }
    }
}

