using System;
using System.Collections.Generic;
using System.Reflection;
using NexusFreelance.Web.Controllers;
using NexusFreelance.Web.Services;
class TestAccountIntegrations
{
    static int failed, checks;
    static void Check(bool ok,string name){checks++;Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failed++;}
    static bool Google(){return (bool)typeof(AccountController).GetMethod("GoogleConfigured",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);}
    static void Set(string key,string value){Environment.SetEnvironmentVariable(key,value);}
    static int Main()
    {
        var keys=new[]{"NEXUS_GOOGLE_CLIENT_ID","NEXUS_GOOGLE_CLIENT_SECRET","NEXUS_GOOGLE_REDIRECT_URI","NEXUS_MAIL_MODE","NEXUS_PUBLIC_BASE_URL","NEXUS_SMTP_HOST","NEXUS_SMTP_PORT","NEXUS_SMTP_FROM","NEXUS_MAIL_PICKUP_DIRECTORY","NEXUS_SMTP_USERNAME","NEXUS_SMTP_PASSWORD"};
        var previous=new Dictionary<string,string>();foreach(var key in keys)previous[key]=Environment.GetEnvironmentVariable(key);
        try {
            Set(keys[0],"synthetic.apps.googleusercontent.com");Set(keys[1],"synthetic-secret");Set(keys[2],"https://localhost:44391/Account/GoogleCallback");
            Check(Google(),"valid Google configuration");
            Set(keys[2],"https://localhost:44391/Account/GoogleCallback#fragment");Check(!Google(),"Google rejects callback fragment");
            Set(keys[2],"https://localhost:44391/Account/GoogleCallback?query=1");Check(!Google(),"Google rejects callback query");
            Set(keys[2],"http://localhost:44391/Account/GoogleCallback");Check(!Google(),"Google requires HTTPS");
            Set(keys[2],"https://localhost:44391/Account/GoogleCallback");Set(keys[1],"");Check(!Google(),"missing Google secret stays unavailable");
            Set("NEXUS_MAIL_MODE","smtp");Set("NEXUS_PUBLIC_BASE_URL","https://localhost:44391");Set("NEXUS_SMTP_HOST","smtp.example.test");Set("NEXUS_SMTP_PORT","587");Set("NEXUS_SMTP_FROM","noreply@example.test");
            Check(PasswordRecoveryService.IsDeliveryConfigured,"valid SMTP configuration");
            Set("NEXUS_SMTP_HOST","smtp.gmail.com");Set("NEXUS_SMTP_USERNAME","");Set("NEXUS_SMTP_PASSWORD","");
            Check(!PasswordRecoveryService.IsDeliveryConfigured,"Gmail without credentials cannot look ready");
            Set("NEXUS_SMTP_USERNAME","sender@gmail.com");Set("NEXUS_SMTP_PASSWORD","synthetic-password");
            Check(PasswordRecoveryService.IsDeliveryConfigured,"Gmail with app credentials can be configured");
            Set("NEXUS_SMTP_PASSWORD","");Check(!PasswordRecoveryService.IsDeliveryConfigured,"Gmail missing app password remains unavailable");
            Set("NEXUS_SMTP_HOST","smtp.example.test");
            Set("NEXUS_SMTP_FROM","invalid-address");Check(!PasswordRecoveryService.IsDeliveryConfigured,"invalid sender cannot look configured");
            Set("NEXUS_SMTP_FROM","noreply@example.test");Set("NEXUS_MAIL_MODE","pickpu");Check(!PasswordRecoveryService.IsDeliveryConfigured,"unknown delivery mode fails closed");
            Set("NEXUS_MAIL_MODE","pickup");Set("NEXUS_MAIL_PICKUP_DIRECTORY",System.IO.Path.GetTempPath());Set("NEXUS_PUBLIC_BASE_URL","http://localhost:44391");
            Check(!PasswordRecoveryService.IsDeliveryConfigured,"pickup also validates HTTPS origin");
            Set("NEXUS_PUBLIC_BASE_URL","https://localhost:44391/unexpected-path");Check(!PasswordRecoveryService.IsDeliveryConfigured,"mail origin must not contain a path");
            Check(!PasswordRecoveryService.PickupPathIsOutside(@"C:\site\web",@"C:\site\web\"),"mail pickup cannot equal web root");
            Check(!PasswordRecoveryService.PickupPathIsOutside(@"C:\site\web\mail",@"C:\site\web"),"mail pickup cannot be beneath web root");
            Check(PasswordRecoveryService.PickupPathIsOutside(@"C:\site\web-mail",@"C:\site\web\"),"a separate sibling mailbox is permitted");
            Console.WriteLine("ACCOUNT_INTEGRATION_CHECKS="+checks+" FAILED="+failed);return failed==0?0:1;
        } finally {foreach(var key in keys)Set(key,previous[key]);}
    }
}
