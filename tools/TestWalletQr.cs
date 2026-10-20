// Runs current wallet sources against a disposable LocalDB. Never touches application data.
using System;
using System.CodeDom.Compiler;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Razor;
using System.Web.Razor.Generator;
using System.Web.Routing;
using Microsoft.CSharp;
using NexusFreelance.Web.Controllers;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;

internal static class TestWalletQr
{
    private static string connection;
    private static int checks;
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    private static void Reject(Action action, string label) { try { action(); } catch (InvalidOperationException) { Check(true, label); return; } throw new Exception(label); }
    private static AppDbContext Db() { var db = new AppDbContext(); db.Database.Connection.ConnectionString = connection; return db; }
    private static UserAccount User(string role, string name) { return new UserAccount { Role = role, Name = name, Email = name + "@example.test", PasswordHash = "test-only", JoinedAtUtc = DateTime.UtcNow }; }
    private sealed class RequestStub : HttpRequestBase {
        public override string UserHostAddress { get { return "127.0.0.1"; } }
        public override string this[string key] { get { return key == "X-Requested-With" ? "XMLHttpRequest" : null; } }
    }
    private sealed class ContextStub : HttpContextBase { public override HttpRequestBase Request { get { return new RequestStub(); } } }
    private sealed class UrlStub : UrlHelper {
        public UrlStub(RequestContext context) : base(context) { }
        public override string Action(string name, object values) { return "/Wallet?transferId=" + new RouteValueDictionary(values)["transferId"]; }
    }
    private static WalletController Controller(UserAccount user)
    {
        var controller = new WalletController(); var type = typeof(BaseController);
        ((AppDbContext)type.GetField("Db", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller)).Database.Connection.ConnectionString = connection;
        type.GetField("activeUser", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, user);
        type.GetField("userLoaded", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);
        controller.ControllerContext = new ControllerContext(new ContextStub(), new RouteData(), controller);
        controller.Url = new UrlStub(controller.ControllerContext.RequestContext);
        return controller;
    }
    private static void CompileView(string root, string name)
    {
        var host = new RazorEngineHost(new CSharpRazorCodeLanguage()) {
            DefaultBaseClass = "System.Web.Mvc.WebViewPage", DefaultClassName = "Wallet" + name,
            GeneratedClassContext = new GeneratedClassContext("Execute", "Write", "WriteLiteral", "WriteTo", "WriteLiteralTo", "System.Web.WebPages.HelperResult", "DefineSection")
        };
        foreach (var ns in new[] { "System", "System.Linq", "System.Collections.Generic", "System.Web.Mvc", "System.Web.Mvc.Html" }) host.NamespaceImports.Add(ns);
        using (var reader = File.OpenText(Path.Combine(root, "NexusFreelance.Web/Views/Wallet/" + name + ".cshtml"))) {
            var generated = new RazorTemplateEngine(host).GenerateCode(reader);
            Check(generated.Success, name + " Razor syntax: " + string.Join("; ", generated.ParserErrors.Select(e => e.Message + " at " + (e.Location.LineIndex + 1))));
            using (var compiler = new CSharpCodeProvider()) {
                var parameters = new CompilerParameters { GenerateInMemory = true };
                foreach (var file in new[] { "System.dll", "System.Core.dll", "System.Web.dll", "System.Web.Extensions.dll", "Microsoft.CSharp.dll" }) parameters.ReferencedAssemblies.Add(file);
                foreach (var file in new[] { "NexusFreelance.Web.dll", "System.Web.Mvc.dll", "System.Web.WebPages.dll", "System.Web.Helpers.dll" }) parameters.ReferencedAssemblies.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file));
                // Alias the current-source harness, even if a parallel app build already contains the new service.
                parameters.CompilerOptions = "/reference:WalletSource=\"" + Assembly.GetExecutingAssembly().Location + "\"";
                var generatedText = new StringWriter(); compiler.GenerateCodeFromCompileUnit(generated.GeneratedCode, generatedText, new CodeGeneratorOptions());
                var viewSource = "extern alias WalletSource;\nusing WalletBankTransferService = WalletSource::NexusFreelance.Web.Services.WalletBankTransferService;\n" + generatedText;
                var result = compiler.CompileAssemblyFromSource(parameters, viewSource);
                Check(!result.Errors.HasErrors, name + " generated view compiles: " + string.Join("; ", result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ErrorText)));
            }
        }
    }
    private static bool Review(int id, int admin)
    {
        using (var db = Db()) { try { new WalletService(db).ReviewMoney(id, admin, true, "Bank receipt verified"); return true; } catch (InvalidOperationException) { return false; } }
    }
    public static int Main(string[] args)
    {
        string database = "NexusWalletQrTests_" + Guid.NewGuid().ToString("N");
        connection = new SqlConnectionStringBuilder { DataSource = @"(localdb)\MSSQLLocalDB", InitialCatalog = database, IntegratedSecurity = true, MultipleActiveResultSets = true }.ConnectionString;
        Database.SetInitializer<AppDbContext>(null);
        try {
            CompileView(args[0], "Index"); CompileView(args[0], "Admin");
            var source = File.ReadAllText(Path.Combine(args[0], "NexusFreelance.Web/Views/Wallet/Index.cshtml"));
            Check(source.Contains("Tạo mã QR") && !source.Contains("Gửi yêu cầu đối soát") && !source.Contains("disabled="), "Client has one enabled amount-to-QR form, no separate manual deposit form");
            foreach (var name in new[] { "Topup", "ContinueTopup", "ReconcileTopup", "RequestMoney", "ReviewMoney" }) {
                var method = typeof(WalletController).GetMethod(name);
                Check(method.IsDefined(typeof(HttpPostAttribute)) && method.IsDefined(typeof(ValidateAntiForgeryTokenAttribute)), name + " is POST and CSRF protected");
            }
            var qr = new MoneyRequest { Kind = "Deposit", Status = "Pending", Amount = 500000, Reference = "NX0123456789ABCDEF0123", Details = WalletBankTransferService.TransferDetails };
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            var payload = WalletBankTransferService.QrPayload(qr);
            Check(payload.StartsWith("000201010212") && payload.Contains("00069704220112290820061111") && payload.Contains("QRIBFTTA"), "QR uses local NAPAS account-transfer payload and MB destination");
            Check(payload.Contains("5406500000") && payload.Contains(qr.Reference) && payload.Contains("5303704"), "QR encodes invariant exact VND amount and server reference");
            int crc=0xffff;
            foreach(var ch in payload.Substring(0,payload.Length-4)) { crc^=ch<<8; for(int bit=0;bit<8;bit++) crc=((crc&0x8000)!=0?(crc<<1)^0x1021:crc<<1)&0xffff; }
            Check(payload.EndsWith(crc.ToString("X4")), "QR checksum covers all fields and CRC header");
            qr.Status = "Approved";
            Reject(() => WalletBankTransferService.QrPayload(qr), "Processed request cannot produce another payment QR");
            qr.Status = "Pending"; qr.Reference = "https://attacker.test";
            Reject(() => WalletBankTransferService.QrPayload(qr), "Arbitrary transfer content cannot become a top-up QR");
            using (var db = Db()) {
                db.Database.Create();
                var client = User("Client", "client" + database); var other = User("Client", "other" + database);
                var admin = User("Admin", "admin" + database); var freelancer = User("Freelancer", "freelancer" + database);
                db.Users.AddRange(new[] { client, other, admin, freelancer }); db.SaveChanges();
                var variableNames = new[] { "NEXUS_VNPAY_ENABLED", "NEXUS_VNPAY_TMN_CODE", "NEXUS_VNPAY_HASH_SECRET", "NEXUS_PUBLIC_BASE_URL", "NEXUS_VNPAY_MODE" };
                var originalVariables = variableNames.ToDictionary(n => n, n => Environment.GetEnvironmentVariable(n));
                try {
                    Environment.SetEnvironmentVariable("NEXUS_VNPAY_ENABLED", "false");
                    using (var controller = Controller(client)) {
                        var result = controller.Topup(800000) as RedirectResult;
                        Check(result != null && result.Url.StartsWith("/Wallet?transferId=") && result.Url.EndsWith("#topup-qr"), "Absent VNPay sends amount to integrated bank QR");
                        var before = db.MoneyRequests.Count(); controller.ModelState.AddModelError("amount", "invalid"); controller.Topup(800000);
                        Check(db.MoneyRequests.Count() == before, "Invalid bound amount cannot create a request");
                        Check(controller.RequestMoney("Deposit", 800000, "crafted-reference", "crafted details") is HttpStatusCodeResult, "Client cannot bypass QR flow through old deposit endpoint");
                    }
                    Environment.SetEnvironmentVariable("NEXUS_VNPAY_ENABLED", "true");
                    Environment.SetEnvironmentVariable("NEXUS_VNPAY_TMN_CODE", "TEST1234");
                    Environment.SetEnvironmentVariable("NEXUS_VNPAY_HASH_SECRET", new string('x', 64));
                    Environment.SetEnvironmentVariable("NEXUS_PUBLIC_BASE_URL", "https://wallet.example.test");
                    Environment.SetEnvironmentVariable("NEXUS_VNPAY_MODE", "sandbox");
                    using (var controller = Controller(client)) {
                        var before = db.MoneyRequests.Count(); var result = controller.Topup(900000) as RedirectResult;
                        var fields = HttpUtility.ParseQueryString(new Uri(result.Url).Query);
                        Check(result.Url.StartsWith("https://sandbox.vnpayment.vn/") && fields["vnp_BankCode"] == "VNPAYQR" && fields["vnp_Amount"] == "90000000", "Configured VNPay opens signed VNPAYQR with exact amount");
                        Check(db.WalletTopups.Count() == 1 && db.MoneyRequests.Count() == before && !db.WalletEntries.Any(), "VNPay route creates no manual deposit and no browser credit");
                    }
                    using (var controller = Controller(other)) Check(controller.ContinueTopup(db.WalletTopups.Single().Id) is HttpNotFoundResult, "Other user cannot resume VNPay order");
                }
                finally { foreach (var name in variableNames) Environment.SetEnvironmentVariable(name, originalVariables[name]); }
                // Remove only this client's unreviewed controller fixture before ledger assertions.
                db.MoneyRequests.RemoveRange(db.MoneyRequests.Where(r => r.UserAccountId == client.Id)); db.SaveChanges();
                var service = new WalletBankTransferService(db);
                foreach (var amount in new[] { -1m, 0m, 9999m, 10000.5m, 10000000000m }) Reject(() => service.Create(client.Id, amount), "Reject invalid amount " + amount);
                Reject(() => service.Create(freelancer.Id, 500000), "Freelancer cannot request a deposit QR");
                client.IsSuspended = true; db.SaveChanges();
                Reject(() => service.Create(client.Id, 500000), "Suspended client cannot request a QR");
                client.IsSuspended = false; db.SaveChanges();
                var request = service.Create(client.Id, 500000);
                Check(request.Status == "Pending" && request.Kind == "Deposit" && !db.WalletEntries.Any() && !db.WalletAccounts.Any(), "Generating a QR creates only a pending request, no balance or ledger credit");
                Check(service.Create(client.Id, 500000).Id == request.Id && db.MoneyRequests.Count() == 1, "Repeated amount submission reuses pending reference");
                Check(service.Find(client.Id, request.Id) != null && service.Find(other.Id, request.Id) == null, "Only owner can reopen bank top-up details");
                var wins = Task.WhenAll(Task.Run(() => Review(request.Id, admin.Id)), Task.Run(() => Review(request.Id, admin.Id))).GetAwaiter().GetResult();
                Check(wins.Count(x => x) == 1, "Concurrent approvals admit exactly one reviewer");
                using (var fresh = Db()) {
                    Check(fresh.WalletAccounts.Single(w => w.UserAccountId == client.Id).Balance == 500000 && fresh.WalletEntries.Count(e => e.Reference == "money:" + request.Id) == 1, "Approval credits exact amount with one ledger entry");
                    Reject(() => new WalletService(fresh).ReviewMoney(request.Id, admin.Id, true, "Repeated receipt"), "Repeated approval is rejected");
                    Reject(() => new WalletService(fresh).ReviewMoney(request.Id, admin.Id, false, "Repeated rejection"), "Approved request cannot be rejected later");
                    Check(fresh.AdminAuditLogs.Count(a => a.Action == "ReviewMoneyRequest") == 1, "Only successful review writes audit log");
                }
                // Use fresh contexts after external commits, as real HTTP requests do.
                using (var fresh = Db()) {
                    var bank = new WalletBankTransferService(fresh);
                    var rejected = bank.Create(client.Id, 600000);
                    Reject(() => new WalletService(fresh).ReviewMoney(rejected.Id, other.Id, true, "Not an admin"), "Non-admin review cannot credit funds");
                    new WalletService(fresh).ReviewMoney(rejected.Id, admin.Id, false, "No matching bank receipt");
                    Reject(() => new WalletService(fresh).ReviewMoney(rejected.Id, admin.Id, true, "Replayed approval"), "Rejected request cannot be approved by replay");
                    Check(fresh.WalletAccounts.Single(w => w.UserAccountId == client.Id).Balance == 500000 && fresh.WalletEntries.Count() == 1, "Rejection never changes balance or ledger");
                    Check(bank.Create(client.Id, 600000).Id != rejected.Id, "Rejected request can be replaced with a new unique reference");
                }
                var creates = Task.WhenAll(Task.Run(() => { using (var fresh = Db()) return new WalletBankTransferService(fresh).Create(other.Id, 700000).Id; }), Task.Run(() => { using (var fresh = Db()) return new WalletBankTransferService(fresh).Create(other.Id, 700000).Id; })).GetAwaiter().GetResult();
                Check(creates[0] == creates[1], "Concurrent equal-amount submissions reuse the same pending request");
                using (var fresh = Db()) {
                    var bank = new WalletBankTransferService(fresh);
                    for (var i = 0; i < 9; i++) bank.Create(other.Id, 20000 + i);
                    Reject(() => bank.Create(other.Id, 30000), "Ten pending requests cap prevents unbounded QR creation");
                    Check(bank.Create(other.Id, 700000).Id == creates[0], "Existing request remains resumable at pending cap");
                }
            }
            Console.WriteLine("Wallet QR: " + checks + " checks passed; no application build/deployment."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally {
            var parsed = new SqlConnectionStringBuilder(connection);
            if (parsed.DataSource != @"(localdb)\MSSQLLocalDB" || parsed.InitialCatalog != database || !database.StartsWith("NexusWalletQrTests_", StringComparison.Ordinal)) throw new Exception("Unsafe test database cleanup");
            using (var db = Db()) if (db.Database.Exists()) db.Database.Delete();
        }
    }
}
