using System.Linq;
using System.Net;
using System.Web.Mvc;
using System.Web.Security;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;

namespace NexusFreelance.Web.Controllers
{
    public abstract class BaseController : Controller
    {
        protected readonly AppDbContext Db = new AppDbContext();
        private UserAccount activeUser;
        private bool userLoaded;
        protected UserAccount CurrentUser
        {
            get
            {
                if (!userLoaded)
                {
                    var accountId = AuthSessionService.AccountId(Request);
                    activeUser = User != null && User.Identity.IsAuthenticated && accountId.HasValue
                        ? Db.Users.SingleOrDefault(u => u.Id == accountId.Value) : null;
                    userLoaded = true;
                }
                return activeUser;
            }
        }

        protected override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            if (User != null && User.Identity.IsAuthenticated &&
                (CurrentUser == null || CurrentUser.IsSuspended ||
                 (PasswordRecoveryService.RequireEmailVerification && !CurrentUser.EmailVerifiedAtUtc.HasValue) ||
                 !AuthSessionService.IsCurrent(Request, CurrentUser)))
            {
                FormsAuthentication.SignOut();
                Response.StatusCode = 403; Response.TrySkipIisCustomErrors = true;
                filterContext.Result = View("~/Views/Shared/Forbidden.cshtml");
                return;
            }
            ViewBag.UnreadCount = CurrentUser == null ? 0 : Db.Notifications.Count(n => n.RecipientId == CurrentUser.Id && !n.ReadAtUtc.HasValue);
            ViewBag.CurrentRole = CurrentUser?.Role;
            ViewBag.CurrentName = CurrentUser?.Name;
            ViewBag.CurrentUserId = CurrentUser?.Id;
            // MVC5 otherwise throws before entering an action when a required numeric
            // route parameter cannot be converted. Reject malformed input, never invoke it.
            var missingValue = filterContext.ActionDescriptor.GetParameters().Any(p =>
                p.ParameterType.IsValueType && System.Nullable.GetUnderlyingType(p.ParameterType) == null &&
                (!filterContext.ActionParameters.ContainsKey(p.ParameterName) || filterContext.ActionParameters[p.ParameterName] == null));
            if (missingValue || (Request.HttpMethod == "GET" && !ModelState.IsValid))
            {
                Response.StatusCode = 400; Response.TrySkipIisCustomErrors = true;
                filterContext.Result = View("~/Views/Shared/InvalidRequest.cshtml");
                return;
            }
            base.OnActionExecuting(filterContext);
        }

        protected ActionResult Forbidden()
        {
            if (Request.IsAjaxRequest()) return new HttpStatusCodeResult(HttpStatusCode.Forbidden);
            Response.StatusCode = 403; Response.TrySkipIisCustomErrors = true;
            return View("~/Views/Shared/Forbidden.cshtml");
        }

        protected override void OnActionExecuted(ActionExecutedContext filterContext)
        {
            var status = filterContext.Result as HttpStatusCodeResult;
            if (status != null && status.StatusCode == 403 && !Request.IsAjaxRequest())
            {
                Response.StatusCode = 403; Response.TrySkipIisCustomErrors = true;
                filterContext.Result = View("~/Views/Shared/Forbidden.cshtml");
            }
            if (status != null && status.StatusCode == 400 && !Request.IsAjaxRequest())
            {
                Response.StatusCode = 400;
                Response.TrySkipIisCustomErrors = true;
                filterContext.Result = View("~/Views/Shared/InvalidRequest.cshtml");
            }
            if (status != null && status.StatusCode == 409 && !Request.IsAjaxRequest())
            {
                Response.StatusCode = 409;
                Response.TrySkipIisCustomErrors = true;
                ViewBag.ConflictReason=status.StatusDescription;
                filterContext.Result = View("~/Views/Shared/Conflict.cshtml");
            }
            base.OnActionExecuted(filterContext);
        }

        protected override void OnException(ExceptionContext filterContext)
        {
            for (var error=filterContext.Exception;error!=null;error=error.InnerException)
            {
                var sql=error as System.Data.SqlClient.SqlException;
                if(sql!=null && sql.Number==1205)
                {
                    Response.StatusCode=409;Response.TrySkipIisCustomErrors=true;
                    filterContext.Result=View("~/Views/Shared/Concurrency.cshtml");
                    filterContext.ExceptionHandled=true;
                    System.Diagnostics.Trace.TraceWarning("SQL deadlock victim rolled back; user may retry.");
                    return;
                }
            }
            base.OnException(filterContext);
        }

        protected override void OnResultExecuted(ResultExecutedContext context)
        {
            base.OnResultExecuted(context);
            if (context.Exception != null || Response.StatusCode != 200 || !(context.Result is ViewResult) || CurrentUser == null) return;
            var controller = ConvertRoute("controller"); var action = ConvertRoute("action");
            if (!ActivityAccessService.ShouldRecord(Request.HttpMethod, Request.IsAjaxRequest(), ControllerContext.IsChildAction, controller, action)) return;
            try { ActivityAccessService.RecordPageAccess(CurrentUser.Id, controller, action); }
            catch (System.Data.Common.DbException) { System.Diagnostics.Trace.TraceWarning("Page access sample was not recorded."); }
            catch (System.Data.Entity.Core.EntityException) { System.Diagnostics.Trace.TraceWarning("Page access sample was not recorded."); }
        }

        private string ConvertRoute(string key) => System.Convert.ToString(RouteData.Values[key], System.Globalization.CultureInfo.InvariantCulture);

        protected override void Dispose(bool disposing)
        {
            if (disposing) Db.Dispose();
            base.Dispose(disposing);
        }
    }
}
