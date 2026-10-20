using System;
using System.Linq;
using System.Web.Helpers;
using System.Web.Mvc;

namespace NexusFreelance.Web.Controllers
{
    // REST resources use the same authenticated account and ownership checks as MVC.
    // No entity serialization: private columns and navigation properties never leave here.
    public class SubmissionApiController : BaseController
    {
        protected override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            if (context.Result is ViewResult && Response.StatusCode >= 400)
                context.Result = Reply(new { error = Response.StatusCode == 400 ? "Invalid request" : "Session expired" }, Response.StatusCode);
        }

        protected override void HandleUnknownAction(string actionName)
        {
            var allow = actionName == "Notification" ? "PATCH" :
                (actionName == "Projects" || actionName == "Notifications" ? "GET" : null);
            if (allow == null) { base.HandleUnknownAction(actionName); return; }
            Response.AppendHeader("Allow", allow);
            Reply(new { error = "Method not allowed" }, 405).ExecuteResult(ControllerContext);
        }

        private ActionResult Reply(object value, int status = 200)
        {
            Response.StatusCode = status;
            Response.TrySkipIisCustomErrors = true;
            Response.SuppressFormsAuthenticationRedirect = true;
            Response.Cache.SetNoStore();
            return Json(value, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public ActionResult Projects(int page = 1, int pageSize = 12, string q = "", string industry = "")
        {
            if (page < 1 || page > 100000 || pageSize < 1 || pageSize > 50 ||
                (q ?? "").Length > 100 || !Services.IndustryCatalog.IsValid(industry))
                return Reply(new { error = "Invalid filters" }, 400);
            var now = DateTime.UtcNow;
            var rows = Db.Projects.Where(p => p.Status == "Published" && (!p.DeadlineUtc.HasValue || p.DeadlineUtc >= now));
            q = (q ?? "").Trim();
            if (q.Length > 0) rows = rows.Where(p => p.Title.Contains(q));
            if (!string.IsNullOrEmpty(industry)) rows = rows.Where(p => p.Industry == industry);
            var total = rows.Count();
            var items = rows.OrderByDescending(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(p => new { id = p.Id, title = p.Title, industry = p.Industry, type = p.ProjectType,
                    budgetVnd = p.Budget, deadlineUtc = p.DeadlineUtc }).ToList();
            return Reply(new { page, pageSize, total, items });
        }

        [HttpGet]
        public ActionResult Notifications(int page = 1, bool unread = false)
        {
            if (CurrentUser == null) return Reply(new { error = "Authentication required" }, 401);
            if (page < 1 || page > 100000) return Reply(new { error = "Invalid page" }, 400);
            var rows = Db.Notifications.Where(n => n.RecipientId == CurrentUser.Id);
            var unreadCount = rows.Count(n => !n.ReadAtUtc.HasValue);
            if (unread) rows = rows.Where(n => !n.ReadAtUtc.HasValue);
            return Reply(new { page, pageSize = 25, total = rows.Count(), unreadCount,
                items = rows.OrderByDescending(n => n.Id).Skip((page - 1) * 25).Take(25)
                    .Select(n => new { id = n.Id, title = n.Title, source = n.Source,
                        createdAtUtc = n.CreatedAtUtc, readAtUtc = n.ReadAtUtc }).ToList() });
        }

        [AcceptVerbs("PATCH")]
        public ActionResult Notification(int id, bool? read)
        {
            if (CurrentUser == null) return Reply(new { error = "Authentication required" }, 401);
            try
            {
                AntiForgery.Validate(Request.Cookies[AntiForgeryConfig.CookieName]?.Value,
                    Request.Headers["X-CSRF-Token"]);
            }
            catch (HttpAntiForgeryException) { return Reply(new { error = "Invalid CSRF token" }, 400); }
            if (id < 1 || read != true) return Reply(new { error = "Only read=true is supported" }, 400);
            var row = Db.Notifications.SingleOrDefault(n => n.Id == id && n.RecipientId == CurrentUser.Id);
            if (row == null) return Reply(new { error = "Not found" }, 404);
            if (!row.ReadAtUtc.HasValue) { row.ReadAtUtc = DateTime.UtcNow; Db.SaveChanges(); }
            return Reply(new { id = row.Id, read = true });
        }
    }
}
