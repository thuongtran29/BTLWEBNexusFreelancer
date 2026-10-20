using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using NexusFreelance.Web.Services;
class TestProtectedSettings
{
    static int count;static DateTime stamp=DateTime.UtcNow;static string path;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    static void Write(string json){var b=Encoding.UTF8.GetBytes(json);File.WriteAllBytes(path,ProtectedData.Protect(b,Encoding.UTF8.GetBytes("NexusFreelance.integrations.v1"),DataProtectionScope.CurrentUser));Array.Clear(b,0,b.Length);File.SetLastWriteTimeUtc(path,stamp=stamp.AddSeconds(2));}
    static int Main(){
        path=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","_local","integrations.dat"));Directory.CreateDirectory(Path.GetDirectoryName(path));
        var key="NEXUS_GOOGLE_CLIENT_ID";var old=Environment.GetEnvironmentVariable(key);Environment.SetEnvironmentVariable(key,null);
        try {
            Write("{\"NEXUS_GOOGLE_CLIENT_ID\":\"synthetic-file\"}");Check(IntegrationSettings.Get(key)=="synthetic-file","encrypted file fallback is used");
            Environment.SetEnvironmentVariable(key,"synthetic-environment");Check(IntegrationSettings.Get(key)=="synthetic-environment","environment has precedence");Environment.SetEnvironmentVariable(key,null);
            Write("{\"NEXUS_GOOGLE_CLIENT_ID\":\"replacement\"}");Check(IntegrationSettings.Get(key)=="replacement","atomic replacement refreshes cache");
            File.WriteAllText(path,"not encrypted");File.SetLastWriteTimeUtc(path,stamp=stamp.AddSeconds(2));Check(IntegrationSettings.Get(key)=="","malformed file fails closed");
            Write("{\"NEXUS_GOOGLE_CLIENT_ID\":\"restored\"}");using(var locked=File.Open(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(IntegrationSettings.Get(key)=="","unreadable file fails closed");
            Write("{\"NEXUS_GOOGLE_CLIENT_ID\":\"restored\"}");Check(IntegrationSettings.Get(key)=="restored","provider recovers after readable replacement");
            Write("{\"NEXUS_GOOGLE_CLIENT_ID\":null}");Check(IntegrationSettings.Get(key)=="","null setting is safely empty");
            Write("{\"NEXUS_REQUIRE_EMAIL_VERIFICATION\":\"false\"}");Check(IntegrationSettings.Get("NEXUS_REQUIRE_EMAIL_VERIFICATION")=="","integration file cannot disable verification policy");
            Write("{\"NEXUS_VNPAY_HASH_SECRET\":\"synthetic-merchant-secret\",\"NEXUS_VNPAY_LIVE_ENABLED\":\"true\",\"NEXUS_PSP_MERCHANT_APPROVED\":\"true\"}");
            Check(IntegrationSettings.Get("NEXUS_VNPAY_HASH_SECRET")=="synthetic-merchant-secret","VNPay uses the same protected configuration as email and AI");
            Check(IntegrationSettings.Get("NEXUS_VNPAY_LIVE_ENABLED")=="" && IntegrationSettings.Get("NEXUS_PSP_MERCHANT_APPROVED")=="","protected setup cannot bypass production payment approvals");
            File.Delete(path);Check(IntegrationSettings.Get(key)=="","deleted config removes cached credentials");
            var shared=System.Configuration.ConfigurationManager.AppSettings["NexusIntegrationStore"];
            var bytes=Encoding.UTF8.GetBytes("{\"NEXUS_GOOGLE_CLIENT_ID\":\"persistent-user-store\"}");
            File.WriteAllBytes(shared,ProtectedData.Protect(bytes,Encoding.UTF8.GetBytes("NexusFreelance.integrations.v1"),DataProtectionScope.CurrentUser));Array.Clear(bytes,0,bytes.Length);
            Check(IntegrationSettings.Get(key)=="persistent-user-store","Visual Studio uses persistent store when project-local file is absent");
            Write("{\"NEXUS_GOOGLE_CLIENT_ID\":\"project-override\"}");Check(IntegrationSettings.Get(key)=="project-override","explicit project configuration takes precedence over user store");
            File.WriteAllText(path,"broken-local-file");Check(IntegrationSettings.Get(key)=="","corrupt explicit project configuration cannot silently use other credentials");
            File.Delete(path);Check(IntegrationSettings.Get(key)=="persistent-user-store","removing project override restores configured persistent store");
            File.Delete(shared);Check(IntegrationSettings.Get(key)=="","deleting persistent store clears cached credentials");
            Console.WriteLine("PROTECTED_SETTINGS_CHECKS="+count);return 0;
        }finally{Environment.SetEnvironmentVariable(key,old);}
    }
}
