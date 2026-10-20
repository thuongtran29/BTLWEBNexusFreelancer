using System;
using System.Collections.Specialized;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using NexusFreelance.Web.Controllers;

public sealed class ConflictProbe : BaseController
{
    public ActionResult Index() { return new EmptyResult(); }
    public void Present(ActionExecutedContext context) { base.OnActionExecuted(context); }
}
public sealed class ProbeRequest : HttpRequestBase
{
    public readonly NameValueCollection Values = new NameValueCollection();
    public override NameValueCollection Headers { get { return Values; } }
    public override string this[string key] { get { return Values[key]; } }
}
public sealed class ProbeResponse : HttpResponseBase
{
    public override int StatusCode { get; set; }
    public override bool TrySkipIisCustomErrors { get; set; }
}
public sealed class ProbeHttp : HttpContextBase
{
    public readonly ProbeRequest Input = new ProbeRequest();
    public readonly ProbeResponse Output = new ProbeResponse();
    public override HttpRequestBase Request { get { return Input; } }
    public override HttpResponseBase Response { get { return Output; } }
}
public static class TestConflictPresentation
{
    private static int count;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        count++; Console.WriteLine("PASS " + label);
    }
    private static ActionExecutedContext Run(ActionResult result, bool ajax, out ProbeHttp http)
    {
        http = new ProbeHttp(); http.Output.StatusCode = 200;
        if (ajax) http.Input.Values["X-Requested-With"] = "XMLHttpRequest";
        using (var controller = new ConflictProbe())
        {
            controller.ControllerContext = new ControllerContext(http, new RouteData(), controller);
            var action = new ReflectedActionDescriptor(typeof(ConflictProbe).GetMethod("Index"), "Index", new ReflectedControllerDescriptor(typeof(ConflictProbe)));
            var context = new ActionExecutedContext(controller.ControllerContext, action, false, null) { Result = result };
            controller.Present(context); return context;
        }
    }
    public static int Main()
    {
        ProbeHttp http;
        var result = Run(new HttpStatusCodeResult(409), false, out http);
        Check(result.Result is ViewResult && ((ViewResult)result.Result).ViewName == "~/Views/Shared/Conflict.cshtml", "ordinary 409 uses friendly view");
        Check(http.Output.StatusCode == 409 && http.Output.TrySkipIisCustomErrors, "409 status retained and IIS replacement suppressed");
        result = Run(new HttpStatusCodeResult(400), false, out http);
        Check(result.Result is ViewResult && ((ViewResult)result.Result).ViewName == "~/Views/Shared/InvalidRequest.cshtml", "ordinary 400 uses an actionable view");
        Check(http.Output.StatusCode == 400 && http.Output.TrySkipIisCustomErrors, "400 rejection retained without a blank IIS page");
        result = Run(new HttpStatusCodeResult(403), false, out http);
        Check(result.Result is ViewResult && ((ViewResult)result.Result).ViewName == "~/Views/Shared/Forbidden.cshtml", "ordinary 403 explains contract access without IIS diagnostics");
        Check(http.Output.StatusCode == 403 && http.Output.TrySkipIisCustomErrors, "403 access rejection is retained");
        foreach (var code in new[] {404,429})
        {
            var original = new HttpStatusCodeResult(code);
            result = Run(original, false, out http);
            Check(Object.ReferenceEquals(result.Result, original), "HTTP " + code + " is not intercepted");
        }
        var ajaxConflict = new HttpStatusCodeResult(409);
        Check(Object.ReferenceEquals(Run(ajaxConflict, true, out http).Result, ajaxConflict), "Ajax conflict unchanged");
        var ajaxForbidden = new HttpStatusCodeResult(403);
        Check(Object.ReferenceEquals(Run(ajaxForbidden, true, out http).Result, ajaxForbidden), "Ajax permission response unchanged");
        var json = new JsonResult { Data = new { RspCode = "00" } };
        Check(Object.ReferenceEquals(Run(json, false, out http).Result, json), "PSP JSON response unchanged");
        Console.WriteLine("CONFLICT_PRESENTATION_TESTS_PASSED checks=" + count);
        return 0;
    }
}
