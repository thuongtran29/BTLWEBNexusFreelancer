using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using NexusFreelance.Web.Services;

class TestExternalSettings
{
    static int count;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    static int Main()
    {
        var path = ConfigurationManager.AppSettings["NexusIntegrationStore"];
        var settings = new Dictionary<string,string> {
            {"NEXUS_MAIL_MODE","smtp"},{"NEXUS_SMTP_HOST","smtp.gmail.com"},{"NEXUS_SMTP_PORT","587"},
            {"NEXUS_SMTP_FROM","synthetic@gmail.com"},{"NEXUS_SMTP_USERNAME","synthetic@gmail.com"},{"NEXUS_SMTP_PASSWORD","synthetic-only"},
            {"NEXUS_PUBLIC_BASE_URL","https://localhost:44391"},{"NEXUS_GOOGLE_CLIENT_ID","synthetic.apps.googleusercontent.com"},
            {"NEXUS_GOOGLE_CLIENT_SECRET","synthetic-only"},{"NEXUS_GOOGLE_REDIRECT_URI","https://localhost:44391/Account/GoogleCallback"},
            {"NEXUS_AI_ENABLED","true"},{"NEXUS_AI_PROVIDER","Gemini"},{"NEXUS_GEMINI_API_KEY","synthetic-only-key-123456789"},{"NEXUS_GEMINI_MODEL","gemini-fixture"},
            {"NEXUS_VNPAY_ENABLED","true"},{"NEXUS_VNPAY_MODE","sandbox"},{"NEXUS_VNPAY_TMN_CODE","TEST0001"},{"NEXUS_VNPAY_HASH_SECRET",new string('s',64)},
            {"NEXUS_VNPAY_LIVE_ENABLED","true"},{"NEXUS_PSP_MERCHANT_APPROVED","true"}
        };
        var previous = new Dictionary<string,string>();
        foreach(var key in settings.Keys) { previous[key] = Environment.GetEnvironmentVariable(key); Environment.SetEnvironmentVariable(key,null); }
        try {
            var plain = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(settings));
            try { File.WriteAllBytes(path,ProtectedData.Protect(plain,Encoding.UTF8.GetBytes("NexusFreelance.integrations.v1"),DataProtectionScope.CurrentUser)); }
            finally { Array.Clear(plain,0,plain.Length); }
            Check(PasswordRecoveryService.IsDeliveryConfigured,"Gmail reads persistent user store without process environment");
            Check(GoogleAuthSettings.IsConfigured,"Google reads persistent client and callback");
            Check(JobDraftGeneratorFactory.IsExternalAiConfigured,"Gemini reads persistent key and model");
            var pay=VnPaySettings.Load();
            Check(pay.MerchantCode=="TEST0001" && pay.PublicBase=="https://localhost:44391","VNPay reads persistent merchant and shared origin");
            Environment.SetEnvironmentVariable("NEXUS_AI_ENABLED","false");
            Check(!JobDraftGeneratorFactory.IsExternalAiConfigured,"Explicit environment can disable AI from persisted configuration");
            Environment.SetEnvironmentVariable("NEXUS_VNPAY_MODE","production");
            Check(!VnPaySettings.IsConfigured,"Persisted approval flags cannot enable production payments");
            Environment.SetEnvironmentVariable("NEXUS_VNPAY_MODE","sandbox");
            Check(VnPaySettings.IsConfigured,"Sandbox remains available without production approval");
            Environment.SetEnvironmentVariable("NEXUS_MAIL_MODE","pickup");
            Check(!PasswordRecoveryService.IsDeliveryConfigured,"Pickup override without mailbox does not silently send SMTP");
            Console.WriteLine("EXTERNAL_SETTINGS_CHECKS="+count+"; no network or database operations");
            return 0;
        } finally { foreach(var key in previous.Keys) Environment.SetEnvironmentVariable(key,previous[key]); }
    }
}
