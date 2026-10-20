using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;
namespace NexusFreelance.Web.Controllers
{
    public class AssessmentsController : BaseController
    {
        [HttpGet, AllowAnonymous]
        public ActionResult Index(string q, string industry, string provider)
        {
            if ((q != null && q.Length > 120) || !IndustryCatalog.IsValid(industry) ||
                (!string.IsNullOrWhiteSpace(provider) && !AssessmentCatalog.All.Any(x => x.Provider == provider)))
                return new HttpStatusCodeResult(400);
            q = (q ?? "").Trim();
            var items = AssessmentCatalog.All.Where(x =>
                (string.IsNullOrWhiteSpace(industry) || x.Industry == industry) &&
                (string.IsNullOrWhiteSpace(provider) || x.Provider == provider) &&
                (q.Length == 0 || (x.Title + " " + x.Summary + " " + x.Provider).IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            return View(new AssessmentCatalogPage { Query=q, Industry=industry, Provider=provider, Resources=items });
        }
    }
}
