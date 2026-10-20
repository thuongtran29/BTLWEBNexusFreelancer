using System;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;

namespace NexusFreelance.Web.Controllers
{
    public class HomeController : BaseController
    {
        [HttpGet]
        public ActionResult Index(string q = "", int page = 1, string industry = "", string type = "", decimal? minBudget = null,
            decimal? maxBudget = null, DateTime? deadlineFrom = null, DateTime? deadlineTo = null, string sort = "newest")
        {
            if (page < 1) page = 1;
            q = (q ?? "").Trim();
            if (q.Length > 100) return new HttpStatusCodeResult(400);
            if (!Services.IndustryCatalog.IsValid(industry) || (type != "" && type != "SOLO" && type != "TEAM" && type != "ENTERPRISE") ||
                (minBudget.HasValue && minBudget < 0) || (maxBudget.HasValue && maxBudget < 0) ||
                (minBudget.HasValue && maxBudget.HasValue && minBudget > maxBudget) ||
                (deadlineFrom.HasValue && deadlineTo.HasValue && deadlineFrom > deadlineTo) ||
                !new[] { "newest", "oldest", "az", "za", "budgetAsc", "budgetDesc", "deadline" }.Contains(sort) || !ModelState.IsValid)
                return new HttpStatusCodeResult(400, "Bộ lọc không hợp lệ.");
            var projects = Db.Projects.Include(p => p.Client).Where(p => p.Status == "Published");
            if (q.Length > 0) projects = projects.Where(p => p.Title.Contains(q) || p.Description.Contains(q));
            if (!string.IsNullOrEmpty(industry)) projects = projects.Where(p => p.Industry == industry);
            if (!string.IsNullOrEmpty(type)) projects = projects.Where(p => p.ProjectType == type);
            if (minBudget.HasValue) projects = projects.Where(p => p.Budget >= minBudget.Value);
            if (maxBudget.HasValue) projects = projects.Where(p => p.Budget <= maxBudget.Value);
            if (deadlineFrom.HasValue) projects = projects.Where(p => p.DeadlineUtc >= deadlineFrom.Value);
            if (deadlineTo.HasValue) projects = projects.Where(p => p.DeadlineUtc <= deadlineTo.Value);
            var total = projects.Count();
            var totalPages = Math.Max(1, (int)((total + 9L) / 10));
            if (page > totalPages) page = totalPages;
            ViewBag.Query = q;
            ViewBag.Page = page;
            ViewBag.Total = total;
            ViewBag.Industry = industry; ViewBag.Type = type; ViewBag.MinBudget = minBudget; ViewBag.MaxBudget = maxBudget;
            ViewBag.DeadlineFrom = deadlineFrom; ViewBag.DeadlineTo = deadlineTo; ViewBag.Sort = sort;
            System.Linq.IOrderedQueryable<Models.Project> ordered;
            switch (sort) {
                case "oldest": ordered = projects.OrderBy(p => p.PublishedAtUtc ?? p.CreatedAtUtc); break;
                case "az": ordered = projects.OrderBy(p => p.Title); break;
                case "za": ordered = projects.OrderByDescending(p => p.Title); break;
                case "budgetAsc": ordered = projects.OrderBy(p => p.Budget); break;
                case "budgetDesc": ordered = projects.OrderByDescending(p => p.Budget); break;
                case "deadline": ordered = projects.OrderBy(p => p.DeadlineUtc == null).ThenBy(p => p.DeadlineUtc); break;
                default: ordered = projects.OrderByDescending(p => p.PublishedAtUtc ?? p.CreatedAtUtc); break;
            }
            return View(ordered.ThenBy(p => p.Id).Skip((page - 1) * 10).Take(10).ToList());
        }

        [HttpGet]
        public ActionResult HowItWorks()
        {
            return View();
        }
    }
}
