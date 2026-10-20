using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.ViewModels;
using NexusFreelance.Web.Services;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public class DashboardController : BaseController
    {
        [HttpGet]
        public ActionResult Index()
        {
            var user = CurrentUser;
            if (user == null) return Forbidden();
            if (user.Role == "Admin") return RedirectToAction("Index", "Admin");
            if (user.Role != "Client" && user.Role != "Freelancer") return Forbidden();

            var page = new DashboardPage
            {
                Name = user.Name,
                Role = user.Role,
                UnreadNotifications = Db.Notifications.Count(n => n.RecipientId == user.Id && !n.ReadAtUtc.HasValue)
            };
            var wallet = Db.WalletAccounts.AsNoTracking().SingleOrDefault(w => w.UserAccountId == user.Id);
            page.WalletBalance = wallet?.Balance ?? 0m; page.WalletHeld = wallet?.Held ?? 0m;
            var contracts = ContractInbox.VisibleTo(Db, user.Id, user.Role);
            page.ContractsToConfirm = ContractInbox.Filter(Db, contracts, "confirm", user.Id, false).Count();
            page.ActiveContracts = ContractInbox.Filter(Db, contracts, "active", user.Id, false).Count();
            page.ContractsToReview = ContractInbox.Filter(Db, contracts, "review", user.Id, false).Count();
            page.DisputedContracts = ContractInbox.Filter(Db, contracts, "issues", user.Id, false).Count();

            if (user.Role == "Client")
            {
                var projects = Db.Projects.AsNoTracking().Where(p => p.ClientId == user.Id);
                page.Projects = projects.Count();
                page.DraftProjects = projects.Count(p => p.Status == "Draft");
                page.PublishedProjects = projects.Count(p => p.Status == "Published");
                page.NextDraftId = projects.Where(p => p.Status == "Draft").OrderByDescending(p => p.Id).Select(p => (int?)p.Id).FirstOrDefault();
                page.ContractsToFund = ContractInbox.Filter(Db, contracts, "fund", user.Id, false).Count();
                page.HasProfileIntroduction = Db.ClientProfiles.Any(p => p.UserAccountId == user.Id && p.About != null && p.About != "");
                page.TotalBudget = projects.Where(p => p.Status != "Cancelled").Select(p => (decimal?)p.Budget).Sum() ?? 0m;
                page.ActiveProjects = projects.Count(p => p.Status == "Published" || p.Status == "InProgress" || p.Status == "AwaitingEscrow" || p.Status == "Disputed");
                page.PendingProposals = Db.Proposals.Count(p => p.Project.ClientId == user.Id && (p.Status == "Pending" || p.Status == "Shortlisted"));
                page.RecentProjects = projects.OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id)
                    .Take(5).Select(p => new DashboardProjectRow
                    {
                        Id = p.Id, Title = p.Title, Status = p.Status,
                        Budget = p.Budget, CreatedAtUtc = p.CreatedAtUtc,
                        WorkspaceId = Db.Workspaces.Where(w => w.ProjectId == p.Id).Select(w => (int?)w.Id).FirstOrDefault()
                    }).ToList();
                // Count outstanding work in workspaces owned by this client only.
                page.OpenTasks = Db.Tasks.Count(t => t.Workspace.Project.ClientId == user.Id && t.Status != "Done");
            }
            else
            {
                page.HasProfileIntroduction = Db.FreelancerProfiles.Any(p => p.UserAccountId == user.Id && p.CoreRole != null && p.CoreRole != "" && p.Bio != null && p.Bio != "");
                ViewBag.ProfileSummary = Db.FreelancerProfiles.SingleOrDefault(p => p.UserAccountId == user.Id);
                ViewBag.CoreSkills = new SmartMatchingService(Db).Evidence(new[] { user.Id })[user.Id];
                var summary = ViewBag.ProfileSummary as Models.FreelancerProfile;
                ViewBag.SelectedRoles = summary == null ? "" : new SmartMatchingService(Db).SelectedRoleLabels(new[] { summary })[user.Id];
                ViewBag.RecommendedProjects = new SmartMatchingService(Db).ForFreelancer(user.Id);
                page.PortfolioItems = Db.PortfolioItems.Count(p => p.FreelancerId == user.Id);
                page.Projects = Db.Proposals.Count(p => p.ApplicantId == user.Id);
                page.PendingProposals = Db.Proposals.Count(p => p.ApplicantId == user.Id && (p.Status == "Pending" || p.Status == "Shortlisted"));
                page.ActiveProjects = Db.TeamMembers.Count(m => m.UserAccountId == user.Id &&
                    (Db.ContractMembers.Any(cm=>cm.UserAccountId==user.Id && cm.Contract.ProjectId==m.Team.ProjectId && cm.Contract.Status=="Active") ||
                    (!Db.Contracts.Any(c=>c.ProjectId==m.Team.ProjectId) && m.Team.Project.Status == "InProgress")));
                page.Invitations = Db.FreelancerGroupMembers.Count(m => m.UserAccountId == user.Id && m.Status == "Invited");
                page.OpenTasks = Db.Tasks.Count(t => t.AssigneeId == user.Id && t.Status != "Done");
                page.RecentProjects = Db.TeamMembers.AsNoTracking().Where(m => m.UserAccountId == user.Id)
                    .OrderByDescending(m => m.Team.Project.CreatedAtUtc).Take(5)
                    .Select(m => new DashboardProjectRow
                    {
                        Id = m.Team.Project.Id,
                        Title = m.Team.Project.Title,
                        Status = m.Team.Project.Status,
                        Budget = m.Team.Project.Budget,
                        CreatedAtUtc = m.Team.Project.CreatedAtUtc,
                        WorkspaceId = Db.Workspaces.Where(w => w.ProjectId == m.Team.ProjectId && (!w.ContractId.HasValue || Db.ContractMembers.Any(c=>c.ContractId==w.ContractId && c.UserAccountId==user.Id))).Select(w => (int?)w.Id).FirstOrDefault()
                    }).ToList();
                page.MyTasks = Db.Tasks.AsNoTracking().Where(t => t.AssigneeId == user.Id && t.Status != "Done")
                    .OrderBy(t => t.DueAtUtc).ThenBy(t => t.Id).Take(5)
                    .Select(t => new DashboardTaskRow
                    {
                        Title = t.Title, Status = t.Status, Priority = t.Priority,
                        DueAtUtc = t.DueAtUtc, WorkspaceId = t.WorkspaceId
                    }).ToList();
            }
            return View(page);
        }
    }
}
