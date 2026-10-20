using System.Linq;
using System.Web.Mvc;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public class ContributionController : BaseController
    {
        [HttpGet]
        public ActionResult Index()
        {
            var user = CurrentUser;
            if (user == null || user.Role != "Freelancer") return Forbidden();
            ViewBag.CoreSkills = new NexusFreelance.Web.Services.SmartMatchingService(Db).Evidence(new[] { user.Id })[user.Id];
            var records = Db.ContributionRecords.Where(c => c.UserAccountId == user.Id)
                .OrderByDescending(c => c.CreatedAtUtc).ToList();
            ViewBag.XpTotal = Db.XPTransactions.Where(x => x.UserAccountId == user.Id)
                .Select(x => (int?)x.Points).Sum() ?? 0;
            ViewBag.RoleByContribution = Db.SkillDNAHistories.Where(h => h.UserAccountId == user.Id)
                .ToList().ToDictionary(h => h.ContributionRecordId, h => h.RoleTitle);
            return View(records);
        }
    }
}
