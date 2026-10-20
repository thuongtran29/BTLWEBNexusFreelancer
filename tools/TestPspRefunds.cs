using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;

// Runs only against disposable HTTP-created contractv2-* fixtures in NexusFreelanceMvp.
// Every outgoing PSP call is intercepted by this handler. No provider network is used.
public static class TestPspRefunds
{
    private static int checks;
    private static void Check(bool ok,string label) {if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
    private static void Denied(Action action,string label) {bool denied=false;try{action();}catch(InvalidOperationException){denied=true;}Check(denied,label);}
    private static decimal Balance(int client) {using(var db=new AppDbContext())return db.WalletAccounts.Where(w=>w.UserAccountId==client).Select(w=>w.Balance).Single();}
    private static PspRefund Read(int id) {using(var db=new AppDbContext())return db.PspRefunds.AsNoTracking().Single(r=>r.Id==id);}
    private sealed class Provider : HttpMessageHandler
    {
        public int Calls;
        public string Result="timeout";
        public Action BeforeResponse;
        public readonly PaymentIntent Payment;
        public decimal Amount;
        public Provider(PaymentIntent payment) {Payment=payment;Amount=payment.Amount;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            Calls++;
            var input=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(await request.Content.ReadAsStringAsync());
            Check(input["vnp_Command"]=="querydr" || input["vnp_Command"]=="refund","provider boundary uses only query/refund API");
            if(BeforeResponse!=null)BeforeResponse();
            if(Result=="timeout")throw new HttpRequestException("Synthetic local transport loss");
            var command=input["vnp_Command"];
            var fields=new Dictionary<string,string> {{"vnp_ResponseId",Guid.NewGuid().ToString("N")},{"vnp_Command",command},{"vnp_ResponseCode","00"},
                {"vnp_Message","Local regression result"},{"vnp_TmnCode",Payment.MerchantCode},{"vnp_TxnRef",Payment.TxnRef},
                {"vnp_Amount",((Result=="original"?Payment.Amount:Amount)*100).ToString("0",CultureInfo.InvariantCulture)},
                {"vnp_BankCode","NCB"},{"vnp_PayDate","20261016120000"},
                {"vnp_TransactionNo",Result=="original"?Payment.ProviderTransactionNo:"9"+Payment.Id.ToString("D8")},
                {"vnp_TransactionType",Result=="original" || Result=="payment"?"01":Amount==Payment.Amount?"02":"03"},
                {"vnp_TransactionStatus",Result=="failure"?"09":"00"},{"vnp_OrderInfo","Local regression outcome"}};
            var order=new[]{"vnp_ResponseId","vnp_Command","vnp_ResponseCode","vnp_Message","vnp_TmnCode","vnp_TxnRef","vnp_Amount","vnp_BankCode","vnp_PayDate","vnp_TransactionNo","vnp_TransactionType","vnp_TransactionStatus","vnp_OrderInfo"};
            var body=string.Join("|",order.Select(k=>fields[k]))+(command=="querydr"?"||":"");
            using(var hmac=new HMACSHA512(Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("NEXUS_VNPAY_HASH_SECRET"))))
                fields["vnp_SecureHash"]=BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).Replace("-","").ToLowerInvariant();
            return new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(new JavaScriptSerializer().Serialize(fields),Encoding.UTF8,"application/json")};
        }
    }
    private static void Scenario(int paymentId,int admin,string mode)
    {
        PaymentIntent payment;int client;
        using(var db=new AppDbContext())
        {
            payment=db.PaymentIntents.AsNoTracking().Single(p=>p.Id==paymentId);
            client=db.Contracts.Where(c=>c.Id==payment.ContractId).Select(c=>c.Project.ClientId).Single();
            var audit = db.Database.Connection.Database == "NexusFreelanceAudit20261019" && db.Users.Any(u=>u.Id==client && u.Email.EndsWith("@nexus.example.test"));
            Check((audit || db.Users.Any(u=>u.Id==client && u.Email.StartsWith("contractv2-"))) && payment.Environment=="sandbox","refund fixture belongs to disposable sandbox client");
        }
        var provider=new Provider(payment);var before=Balance(client);var amount=mode=="partial"?payment.Amount/2:payment.Amount;provider.Amount=amount;
        int refundId;
        using(var db=new AppDbContext())
        {
            var service=new PspPaymentService(db,provider);
            Denied(()=>service.QueueRefund(paymentId,client,amount,"Local non-admin must not initiate refund"),"non-admin cannot queue refund");
            var merchant=Environment.GetEnvironmentVariable("NEXUS_VNPAY_TMN_CODE");
            try
            {
                Environment.SetEnvironmentVariable("NEXUS_VNPAY_TMN_CODE","OTHER001");
                Denied(()=>service.QueueRefund(paymentId,admin,amount,"Wrong merchant cannot reserve customer money"),"wrong merchant cannot reserve a refund");
                Check(Balance(client)==before && !db.PspRefunds.Any(r=>r.PaymentIntentId==paymentId),"configuration mismatch leaves wallet and queue unchanged");
            }
            finally {Environment.SetEnvironmentVariable("NEXUS_VNPAY_TMN_CODE",merchant);}
            refundId=service.QueueRefund(paymentId,admin,amount,"Disposable refund integration fixture only");
            Check(Read(refundId).Status=="Queued" && Balance(client)==before-amount,"durable queue reserves available money exactly once");
            Check(service.QueueRefund(paymentId,admin,amount,"Duplicate refund creation must reuse reservation")==refundId && Balance(client)==before-amount,"duplicate queue reuses persisted request and reservation");
            Denied(()=>service.QueueRefund(paymentId,admin,amount-1,"Different amount cannot reuse refund request"),"changed duplicate amount is rejected");
        }
        // Dispose/reopen context to prove queued work survives process/request loss.
        provider.Result=mode=="unknown"?"timeout":mode=="failure"?"failure":"success";
        using(var db=new AppDbContext())new PspPaymentService(db,provider).ProcessRefund(refundId,"127.0.0.1").GetAwaiter().GetResult();
        Check(provider.Calls==1,"one durable refund request makes one transfer attempt");
        if(mode=="unknown")
        {
            Check(Read(refundId).Status=="Unknown" && Balance(client)==before-amount,"lost response retains unknown reservation");
            using(var db=new AppDbContext())new PspPaymentService(db,provider).ProcessRefund(refundId,"127.0.0.1").GetAwaiter().GetResult();
            Check(provider.Calls==1,"unknown refund cannot be blindly resent");
            provider.Result="original";
            using(var db=new AppDbContext())new PspPaymentService(db,provider).ReconcileRefund(refundId,admin,"127.0.0.1").GetAwaiter().GetResult();
            Check(Read(refundId).Status=="Unknown" && Balance(client)==before-amount,"original payment success is not proof of refund");
            provider.Result="success";
            using(var db=new AppDbContext())new PspPaymentService(db,provider).ReconcileRefund(refundId,admin,"127.0.0.1").GetAwaiter().GetResult();
            Check(Read(refundId).Status=="Succeeded" && Balance(client)==before-amount,"signed matching refund query settles without another debit");
        }
        else if(mode=="failure")Check(Read(refundId).Status=="Failed" && Balance(client)==before,"signed terminal rejection restores reserved money");
        else Check(Read(refundId).Status=="Succeeded" && Balance(client)==before-amount,"partial refund debits only its reserved amount");
        var calls=provider.Calls;
        var requestId=Read(refundId).RequestId;
        using(var db=new AppDbContext())
        {
            var service=new PspPaymentService(db,provider);
            service.ProcessRefund(refundId,"127.0.0.1").GetAwaiter().GetResult();
            service.ReconcileRefund(refundId,admin,"127.0.0.1").GetAwaiter().GetResult();
            Check(provider.Calls==calls,"terminal refund replay never calls provider again");
            Check(db.WalletEntries.Count(e=>e.Reference=="vnpay:refund:"+requestId)==1,"one reservation journal per refund");
            Check(db.WalletEntries.Count(e=>e.Reference=="vnpay:refund-restore:"+refundId)==(mode=="failure"?1:0),"restoration journal exists only once for terminal failure");
        }
    }
    public static int Main(string[] args)
    {
        if(args.Length==5 && args[0]=="--audit-fixtures") return AuditScenarios(args);
        if(args.Length==4 && args[0]=="--capture-dispute-race")return CaptureRace(int.Parse(args[1]),int.Parse(args[2]),int.Parse(args[3]));
        if(args.Length!=5 || args[0]!="--disposable-local-fixtures")return 2;
        Database.SetInitializer<AppDbContext>(null);
        int admin=int.Parse(args[1]);
        using(var db=new AppDbContext())
        {
            if(db.Database.Connection.Database!="NexusFreelanceMvp" || !db.Users.Any(u=>u.Id==admin && u.Role=="Client" && u.Email.StartsWith("contractv2-") && !u.IsSuspended))return 2;
            db.Database.ExecuteSqlCommand("UPDATE dbo.Users SET Role='Admin' WHERE Id=@p0 AND Role='Client' AND Email LIKE 'contractv2-%@example.test'",admin);
        }
        try
        {
            Scenario(int.Parse(args[2]),admin,"unknown");Scenario(int.Parse(args[3]),admin,"failure");Scenario(int.Parse(args[4]),admin,"partial");
            Console.WriteLine("PSP_REFUNDS_SQL_PASSED checks="+checks);return 0;
        }
        catch(Exception ex) {Console.Error.WriteLine("PSP_REFUNDS_SQL_FAILED "+ex.GetType().Name+": "+ex.Message);return 1;}
        finally {using(var db=new AppDbContext())db.Database.ExecuteSqlCommand("UPDATE dbo.Users SET Role='Client' WHERE Id=@p0 AND Role='Admin' AND Email LIKE 'contractv2-%@example.test'",admin);}
    }
    private static int AuditScenarios(string[] args)
    {
        Database.SetInitializer<AppDbContext>(null);
        var admin = int.Parse(args[1]);
        using(var db=new AppDbContext())
        {
            if(db.Database.Connection.Database!="NexusFreelanceAudit20261019" || !db.Users.Any(u=>u.Id==admin && u.Role=="Admin" && !u.IsSuspended) ||
                Environment.GetEnvironmentVariable("NEXUS_VNPAY_TMN_CODE")!="AUDIT020" || Environment.GetEnvironmentVariable("NEXUS_VNPAY_MODE")!="sandbox") return 2;
        }
        try
        {
            Scenario(int.Parse(args[2]),admin,"unknown");Scenario(int.Parse(args[3]),admin,"failure");Scenario(int.Parse(args[4]),admin,"partial");
            Console.WriteLine("PSP_REFUNDS_AUDIT_PASSED checks="+checks);return 0;
        }
        catch(Exception ex) {Console.Error.WriteLine("PSP_REFUNDS_AUDIT_FAILED "+ex.GetType().Name+": "+ex.Message);return 1;}
    }
    private static int CaptureRace(int paymentId,int client,int projectId)
    {
        Database.SetInitializer<AppDbContext>(null);
        try
        {
            using(var db=new AppDbContext())
            {
                Check(db.Database.Connection.Database=="NexusFreelanceMvp" && db.Users.Any(u=>u.Id==client && u.Email.StartsWith("contractv2-")),"capture race uses disposable local fixture");
                // Matches the controller: contract/milestone tracked BEFORE the external await.
                var payment=db.PaymentIntents.Include(p=>p.Contract.Project).Include(p=>p.Milestone).Single(p=>p.Id==paymentId);
                Check(payment.Contract.ProjectId==projectId && payment.Contract.Project.ClientId==client && payment.Contract.Status=="Active" && payment.Milestone.Status=="Planned","race starts with active tracked contract and planned second milestone");
                decimal balance,held;
                using(var oracle=new AppDbContext()){var wallet=oracle.WalletAccounts.Single(w=>w.UserAccountId==client);balance=wallet.Balance;held=wallet.Held;}
                var provider=new Provider(payment) {Result="payment"};var disputed=false;
                provider.BeforeResponse=()=>{if(!disputed){using(var other=new AppDbContext())new WalletService(other).Dispute(projectId,client,"Disposable race: dispute commits during provider query",payment.ContractId);disputed=true;}};
                var service=new PspPaymentService(db,provider);
                service.Reconcile(paymentId,client,"127.0.0.1").GetAwaiter().GetResult();
                using(var oracle=new AppDbContext())
                {
                    var stored=oracle.PaymentIntents.Single(p=>p.Id==paymentId);var wallet=oracle.WalletAccounts.Single(w=>w.UserAccountId==client);
                    Check(disputed && oracle.Contracts.Single(c=>c.Id==payment.ContractId).Status=="Disputed","concurrent dispute remains committed");
                    Check(stored.Status=="PaidUnallocated" && oracle.EscrowMilestones.Single(m=>m.Id==payment.EscrowMilestoneId).Status=="Planned","reconciliation reloads disputed contract instead of allocating stale active state");
                    Check(wallet.Balance==balance+payment.Amount && wallet.Held==held,"late query receives money once without enlarging disputed hold");
                }
                Check(service.Reconcile(paymentId,client,"127.0.0.1").GetAwaiter().GetResult()=="02","query replay is duplicate after unallocated capture");
                using(var oracle=new AppDbContext())Check(oracle.WalletEntries.Count(e=>e.Reference=="vnpay:capture:"+paymentId)==1,"concurrent-state reconciliation retains one capture journal");
            }
            Console.WriteLine("PSP_CAPTURE_RACE_PASSED checks="+checks);return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("PSP_CAPTURE_RACE_FAILED "+ex.GetType().Name+": "+ex.Message);return 1;}
    }
}
