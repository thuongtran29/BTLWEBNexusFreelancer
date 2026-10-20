using System.Web;
using System.Web.Mvc;

namespace NexusFreelance.Web
{
    public static class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
            filters.Add(new AntiForgeryErrorFilter());
            filters.Add(new RequireHttpsAttribute { Order = 1 });
        }
    }

    // Reject invalid tokens before the action runs. A stale sign-in form can recover
    // through a new GET; no credentials or return URL are replayed.
    public sealed class AntiForgeryErrorFilter : FilterAttribute, IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (context.ExceptionHandled) return;
            var error = context.Exception;
            while (error != null)
            {
                if (error.GetType().Name == "HttpAntiForgeryException" ||
                    error.GetType().Name == "AntiForgeryException")
                {
                    var controller = context.RouteData.Values["controller"] as string;
                    var action = context.RouteData.Values["action"] as string;
                    var recoveryAction = SafeAccountForm(action);
                    if (string.Equals(controller, "Account", System.StringComparison.OrdinalIgnoreCase) && recoveryAction != null &&
                        string.Equals(context.HttpContext.Request.HttpMethod, "POST", System.StringComparison.OrdinalIgnoreCase))
                    {
                        context.Controller.TempData["Notice"] = recoveryAction == "Login"
                            ? "Phiên đăng nhập đã thay đổi hoặc biểu mẫu đã hết hiệu lực. Vui lòng chọn lại vai trò và nhập thông tin trong biểu mẫu đăng nhập mới."
                            : "Biểu mẫu đã hết hiệu lực hoặc phiên làm việc đã thay đổi. Yêu cầu vừa gửi chưa được thực hiện. Vui lòng nhập lại thông tin trên biểu mẫu mới.";
                        context.Result = new RedirectToRouteResult(new System.Web.Routing.RouteValueDictionary(new { controller = "Account", action = recoveryAction }));
                        context.HttpContext.Response.Cache.SetCacheability(HttpCacheability.NoCache);
                        context.HttpContext.Response.Cache.SetNoStore();
                    }
                    else
                    {
                        context.HttpContext.Response.StatusCode = 400;
                        context.HttpContext.Response.TrySkipIisCustomErrors = true;
                        context.Result = new ViewResult { ViewName = "~/Views/Shared/RequestExpired.cshtml" };
                    }
                    context.ExceptionHandled = true;
                    return;
                }
                if (error is HttpRequestValidationException)
                {
                    context.HttpContext.Response.StatusCode = 400;
                    context.HttpContext.Response.TrySkipIisCustomErrors = true;
                    context.Result = new ViewResult { ViewName = "~/Views/Shared/InvalidRequest.cshtml" };
                    context.ExceptionHandled = true;
                    return;
                }
                error = error.InnerException;
            }
        }
        private static string SafeAccountForm(string action)
        {
            if (string.Equals(action, "Login", System.StringComparison.OrdinalIgnoreCase) || string.Equals(action, "GoogleLogin", System.StringComparison.OrdinalIgnoreCase)) return "Login";
            if (string.Equals(action, "ForgotPassword", System.StringComparison.OrdinalIgnoreCase) || string.Equals(action, "ResetPassword", System.StringComparison.OrdinalIgnoreCase)) return "ForgotPassword";
            if (string.Equals(action, "ResendVerification", System.StringComparison.OrdinalIgnoreCase) || string.Equals(action, "ConfirmRegistration", System.StringComparison.OrdinalIgnoreCase) || string.Equals(action, "ResendRegistrationCode", System.StringComparison.OrdinalIgnoreCase) || string.Equals(action, "VerifyEmail", System.StringComparison.OrdinalIgnoreCase)) return "ResendVerification";
            return null;
        }
    }
}
