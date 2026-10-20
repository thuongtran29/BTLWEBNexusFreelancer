using System;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using NexusFreelance.Web.Controllers;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;
class RecoveryHttp : HttpContextBase
{
    class Input : HttpRequestBase { public override string UserHostAddress {get{return "recovery-unit-test";}} }
    class Output : HttpResponseBase {public override int StatusCode {get;set;} public override bool TrySkipIisCustomErrors {get;set;}}
    readonly Input input=new Input();readonly Output output=new Output();
    public override HttpRequestBase Request {get{return input;}}public override HttpResponseBase Response {get{return output;}}
}
class TestRecoveryErrors
{
    static int checks;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static AccountController Open(out RecoveryHttp http){http=new RecoveryHttp();var c=new AccountController();c.ControllerContext=new ControllerContext(http,new RouteData(),c);return c;}
    static int Main()
    {
        var mode=Environment.GetEnvironmentVariable("NEXUS_MAIL_MODE");
        try {
            RecoveryHttp http;
            Environment.SetEnvironmentVariable("NEXUS_MAIL_MODE","unavailable-test-mode");
            using(var c=Open(out http)){
                var form=new ForgotPasswordForm{Email="unknown@example.test",Role="Freelancer"};var result=c.ForgotPassword(form) as ViewResult;
                Check(result!=null && result.Model==form && !c.ModelState.IsValid,"delivery outage shows error and preserves selected role/email");
                Check(http.Response.StatusCode==503 && http.Response.TrySkipIisCustomErrors,"delivery outage retains usable HTTP 503 view");
                Check(c.ViewData["RequestAccepted"]==null,"unconfigured email never claims acceptance");
            }
            using(var c=Open(out http)){c.ForgotPassword(new ForgotPasswordForm{Email="unknown@example.test"});Check(!c.ModelState.IsValid,"missing role rejected before account lookup");}
            Environment.SetEnvironmentVariable("NEXUS_MAIL_MODE","pickup");
            Environment.SetEnvironmentVariable("NEXUS_MAIL_PICKUP_DIRECTORY",System.IO.Path.GetTempPath());
            Environment.SetEnvironmentVariable("NEXUS_PUBLIC_BASE_URL","https://localhost:44391");
            var email="limited-"+Guid.NewGuid().ToString("N")+"@example.test";
            for(var i=0;i<3;i++)PasswordResetLimiter.TryRecord(email,"recovery-unit-test");
            using(var c=Open(out http)){
                var form=new ForgotPasswordForm{Email=email,Role="Client"};c.ForgotPassword(form);
                Check(http.Response.StatusCode==429 && !c.ModelState.IsValid,"rate limit is visible before account lookup");
                Check(c.ViewData["RequestAccepted"]==null,"rate limited request is not shown as accepted");
            }
            Console.WriteLine("RECOVERY_ERROR_CHECKS="+checks);return 0;
        } finally {Environment.SetEnvironmentVariable("NEXUS_MAIL_MODE",mode);}
    }
}
