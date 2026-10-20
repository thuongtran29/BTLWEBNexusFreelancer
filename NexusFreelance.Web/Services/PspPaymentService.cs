using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
namespace NexusFreelance.Web.Services
{
    public sealed class PspPaymentService
    {
        private readonly AppDbContext db;
        private readonly HttpMessageHandler transport;
        public PspPaymentService(AppDbContext database,HttpMessageHandler testTransport=null) {db=database;transport=testTransport;}
        private static void Must(bool ok,string message) {if(!ok)throw new InvalidOperationException(message);}
        private VnPaySettings Settings()
        {
            var settings=VnPaySettings.Load();
            Must(settings.Environment!="production" || !(string.Equals(db.Database.Connection.Database,"NexusFreelanceMvp",StringComparison.OrdinalIgnoreCase) || db.Database.Connection.Database.StartsWith("NexusFreelanceSubmission",StringComparison.OrdinalIgnoreCase)),"VNPay production phải sử dụng CSDL vận hành riêng, không dùng số dư giả lập của CSDL phát triển.");
            return settings;
        }
        private void LockProject(int projectId) {db.Database.SqlQuery<int>("SELECT Id FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0",projectId).Single();}
        public PaymentIntent Checkout(int milestoneId,int clientId,string ip)
        {
            var settings=Settings();var projectId=db.EscrowMilestones.Where(m=>m.Id==milestoneId).Select(m=>(int?)m.ProjectId).SingleOrDefault();Must(projectId.HasValue,"Mốc không tồn tại.");
            using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                LockProject(projectId.Value);
                var m=db.EscrowMilestones.Include(x=>x.Contract.Project).Single(x=>x.Id==milestoneId);db.Entry(m).Reload();
                Must(m.ContractId.HasValue && m.Contract.Project.ClientId==clientId,"Không có quyền thanh toán mốc hợp đồng này.");
                new ContractService(db).RequireFundingReady(m.ContractId.Value);
                Must(m.Status=="Planned" && m.Amount<=9999999999m,"Mốc đã được bảo đảm hoặc số tiền vượt giới hạn VNPay.");
                var existing=db.PaymentIntents.SingleOrDefault(p=>p.EscrowMilestoneId==milestoneId && p.Status=="Pending");
                if(existing!=null)
                {
                    Must(existing.ExpiresAtUtc>DateTime.UtcNow && existing.MerchantCode==settings.MerchantCode && existing.Environment==settings.Environment,"Giao dịch cũ cần được đối soát trước khi tạo yêu cầu mới.");
                    tx.Commit();return existing;
                }
                var now=DateTime.UtcNow;
                var payment=new PaymentIntent {ContractId=m.ContractId.Value,EscrowMilestoneId=m.Id,TxnRef=Guid.NewGuid().ToString("N"),Status="Pending",Amount=m.Amount,Currency="VND",Environment=settings.Environment,
                    MerchantCode=settings.MerchantCode,TransactionDate=VnPayCodec.VietnamDate(now),CreatedAtUtc=now,ExpiresAtUtc=now.AddMinutes(15)};
                payment.CheckoutUrl=VnPayCodec.Checkout(payment,settings,ip);
                db.PaymentIntents.Add(payment);db.SaveChanges();tx.Commit();return payment;
            }
        }
        public string ReceiveIpn(IDictionary<string,string> fields)
        {
            var settings=Settings();
            if(!VnPayCodec.VerifyQuery(fields,settings))return "97";
            return Record(fields,settings,"IPN");
        }
        private string Record(IDictionary<string,string> fields,VnPaySettings settings,string source)
        {
            var reference=VnPayCodec.Get(fields,"vnp_TxnRef");var code=VnPayCodec.Get(fields,"vnp_ResponseCode");var status=VnPayCodec.Get(fields,"vnp_TransactionStatus");
            if(!Regex.IsMatch(reference,"^[a-fA-F0-9]{32}$") || !Regex.IsMatch(code,"^[0-9]{2}$") || !Regex.IsMatch(status,"^[0-9]{2}$") || VnPayCodec.Get(fields,"vnp_TmnCode")!=settings.MerchantCode)return "97";
            var projectId=db.PaymentIntents.Where(p=>p.TxnRef==reference).Select(p=>(int?)p.Contract.ProjectId).SingleOrDefault();if(!projectId.HasValue)return "01";
            using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                WalletTopupService.LockProviderTransaction(db,settings,VnPayCodec.Get(fields,"vnp_TransactionNo"));
                LockProject(projectId.Value);
                var payment=db.PaymentIntents.Include(p=>p.Contract.Project).Include(p=>p.Milestone).Single(p=>p.TxnRef==reference);db.Entry(payment).Reload();
                if(payment.Environment!=settings.Environment || payment.MerchantCode!=settings.MerchantCode)return "97";
                decimal amount;var currency=VnPayCodec.Get(fields,"vnp_CurrCode");
                if(!decimal.TryParse(VnPayCodec.Get(fields,"vnp_Amount"),NumberStyles.None,CultureInfo.InvariantCulture,out amount) || amount!=payment.Amount*100 || (currency!="" && currency!=payment.Currency))return "04";
                if(payment.Status=="Paid" || payment.Status=="PaidUnallocated")return "02";
                var fingerprint=ContractService.Digest(source+"|"+reference+"|"+VnPayCodec.Get(fields,"vnp_SecureHash"));
                if(db.PspReceipts.Any(r=>r.Digest==fingerprint))return "02";
                var transaction=VnPayCodec.Get(fields,"vnp_TransactionNo");
                if(code=="00" && status=="00")
                {
                    if(!Regex.IsMatch(transaction,"^[0-9]{1,15}$") || transaction.All(c=>c=='0'))return "97";
                    if(db.WalletTopups.Any(t=>t.Environment==settings.Environment && t.MerchantCode==settings.MerchantCode && t.ProviderTransactionNo==transaction))return "97";
                    if(db.PaymentIntents.Any(p=>p.Id!=payment.Id && p.Environment==payment.Environment && p.MerchantCode==payment.MerchantCode && p.ProviderTransactionNo==transaction))return "97";
                    payment.ProviderTransactionNo=transaction;
                    var allocated=new WalletService(db).CaptureProviderUnderLock(payment);
                    payment.Status=allocated?"Paid":"PaidUnallocated";payment.PaidAtUtc=DateTime.UtcNow;
                    NotificationService.Queue(db,payment.Contract.Project.ClientId,allocated?"VNPay đã xác nhận bảo đảm mốc #"+payment.EscrowMilestoneId:"VNPay đã nhận tiền; cần phân bổ hoặc hoàn khoản chưa phân bổ","/Contracts/Details/"+payment.ContractId);
                }
                else payment.Status=status=="01" && code=="00"?"Pending":"Failed";
                payment.ResponseCode=code;
                db.PspReceipts.Add(new PspReceipt {PaymentIntentId=payment.Id,Digest=fingerprint,Source=source,ResponseCode=code,ReceivedAtUtc=DateTime.UtcNow});
                db.SaveChanges();tx.Commit();return "00";
            }
        }
        public async Task<string> Reconcile(int paymentId,int actorId,string ip)
        {
            var payment=db.PaymentIntents.AsNoTracking().Include(p=>p.Contract.Project).SingleOrDefault(p=>p.Id==paymentId);
            Must(payment!=null,"Không tìm thấy giao dịch.");
            Must(payment.Contract.Project.ClientId==actorId || db.Users.Any(u=>u.Id==actorId && u.Role=="Admin" && !u.IsSuspended),"Không có quyền đối soát.");
            var settings=Settings();var response=await new VnPayGateway(settings,transport).Send(payment,null,ip);
            Must(VnPayCodec.Get(response,"vnp_Command")=="querydr" && VnPayCodec.Get(response,"vnp_ResponseCode")=="00" && VnPayCodec.Get(response,"vnp_TransactionType")=="01" && VnPayCodec.Get(response,"vnp_TxnRef")==payment.TxnRef,"VNPay chưa trả kết quả thanh toán xác định; không thay đổi tiền.");
            var result=Record(response,settings,"QueryDR");Must(result=="00" || result=="02","Không thể đối chiếu dữ liệu giao dịch với VNPay.");return result;
        }
        public int QueueRefund(int paymentId,int adminId,decimal amount,string reason)
        {
            var settings=Settings();Must(WalletService.ValidAmount(amount) && !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length>=15 && reason.Length<=300,"Số tiền hoặc lý do hoàn tiền không hợp lệ.");
            var projectId=db.PaymentIntents.Where(p=>p.Id==paymentId).Select(p=>(int?)p.Contract.ProjectId).SingleOrDefault();Must(projectId.HasValue,"Giao dịch không tồn tại.");
            using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                LockProject(projectId.Value);Must(db.Users.Any(u=>u.Id==adminId && u.Role=="Admin" && !u.IsSuspended),"Chỉ quản trị đối soát được gửi yêu cầu hoàn tiền.");
                var payment=db.PaymentIntents.Include(p=>p.Contract.Project).Include(p=>p.Milestone).Single(p=>p.Id==paymentId);db.Entry(payment).Reload();
                Must(payment.Environment==settings.Environment && payment.MerchantCode==settings.MerchantCode,"Cần cấu hình đúng môi trường và merchant của giao dịch gốc trước khi giữ tiền hoàn.");
                Must((payment.Status=="Paid" || payment.Status=="PaidUnallocated") && amount<=payment.Amount,"Không vượt khoản đã được VNPay xác nhận.");
                var existing=db.PspRefunds.SingleOrDefault(r=>r.PaymentIntentId==paymentId);
                if(existing!=null){Must(existing.Amount==amount,"Giao dịch đã có yêu cầu hoàn với số tiền khác.");tx.Commit();return existing.Id;}
                Must(payment.Status=="PaidUnallocated" || (payment.Milestone.SettledAtUtc.HasValue && payment.Milestone.Refunded>=amount),"Cần quyết định hoàn tiền của mốc trước khi gửi lệnh hoàn qua PSP.");
                var refund=new PspRefund {PaymentIntentId=paymentId,RequestedById=adminId,Amount=amount,Reason=reason.Trim(),RequestId=Guid.NewGuid().ToString("N"),Status="Queued",CreatedAtUtc=DateTime.UtcNow};
                db.PspRefunds.Add(refund);
                new WalletService(db).ReserveProviderRefundUnderLock(payment,amount,refund.RequestId);
                db.AdminAuditLogs.Add(new AdminAuditLog {ActorId=adminId,TargetUserId=payment.Contract.Project.ClientId,Action="RequestPspRefund",Reason="Payment #"+paymentId+": "+amount.ToString("0",CultureInfo.InvariantCulture)+" VND. "+reason.Trim().Substring(0,Math.Min(reason.Trim().Length,120)),CreatedAtUtc=DateTime.UtcNow});
                db.SaveChanges();tx.Commit();return refund.Id;
            }
        }
        // Durable queue claim before the HTTP boundary. A crash/timeout never causes blind resubmission.
        public async Task ProcessRefund(int id,string ip)
        {
            var settings=Settings();
            var projectId=db.PspRefunds.Where(r=>r.Id==id).Select(r=>(int?)r.Payment.Contract.ProjectId).SingleOrDefault();Must(projectId.HasValue,"Không tìm thấy yêu cầu hoàn.");
            PspRefund refund;
            using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                LockProject(projectId.Value);refund=db.PspRefunds.Include(r=>r.Payment.Contract.Project).Single(r=>r.Id==id);db.Entry(refund).Reload();
                if(refund.Status!="Queued") {tx.Commit();return;}
                Must(refund.Payment.Environment==settings.Environment && refund.Payment.MerchantCode==settings.MerchantCode,"Cấu hình VNPay khác giao dịch hoàn đang chờ.");
                refund.Status="Processing";refund.AttemptedAtUtc=DateTime.UtcNow;db.SaveChanges();tx.Commit();
            }
            Dictionary<string,string> response=null;
            try {response=await new VnPayGateway(settings,transport).Send(refund.Payment,refund,ip);}
            catch(Exception ex) when(ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException || ex is ArgumentException || ex is System.IO.IOException)
            {System.Diagnostics.Trace.TraceWarning("VNPay refund outcome unknown; reconcile the persisted request before any further transfer.");}
            using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                LockProject(projectId.Value);db.Entry(refund).Reload();
                if(refund.Status!="Processing"){tx.Commit();return;}
                refund.Status="Unknown";
                if(response!=null)ApplyRefundResponse(refund,response,"refund");
                db.SaveChanges();tx.Commit();
            }
        }
        private void ApplyRefundResponse(PspRefund refund,IDictionary<string,string> response,string command)
        {
            var outcome=VnPayRefundOutcome.Classify(refund,response,command);
            if(outcome=="Unknown")return;
            refund.ResponseCode=VnPayCodec.Get(response,"vnp_ResponseCode");
            refund.ResponseDigest=ContractService.Digest(VnPayCodec.Canonical(response));
            refund.Status=outcome;
            if(outcome=="Succeeded")refund.ProviderTransactionNo=VnPayCodec.Get(response,"vnp_TransactionNo");
            else new WalletService(db).RestoreProviderRefundUnderLock(refund);
        }
        public async Task ReconcileRefund(int id,int adminId,string ip)
        {
            Must(db.Users.Any(u=>u.Id==adminId && u.Role=="Admin" && !u.IsSuspended),"Chỉ quản trị được đối soát hoàn tiền.");
            var refund=db.PspRefunds.Include(r=>r.Payment.Contract.Project).SingleOrDefault(r=>r.Id==id);
            Must(refund!=null,"Không tìm thấy yêu cầu hoàn.");
            if(refund.Status=="Succeeded" || refund.Status=="Failed")return;
            Must(refund.Status=="Unknown" || (refund.Status=="Processing" && refund.AttemptedAtUtc<DateTime.UtcNow.AddMinutes(-1)),"Yêu cầu đang được gửi hoặc còn trong hàng đợi.");
            // This only queries the provider. A lost refund response must never trigger another refund wire call.
            var response=await new VnPayGateway(Settings(),transport).Send(refund.Payment,null,ip);
            using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                LockProject(refund.Payment.Contract.ProjectId);db.Entry(refund).Reload();
                if(refund.Status=="Succeeded" || refund.Status=="Failed"){tx.Commit();return;}
                refund.Status="Unknown";refund.ReconciledAtUtc=DateTime.UtcNow;
                ApplyRefundResponse(refund,response,"querydr");
                db.AdminAuditLogs.Add(new AdminAuditLog {ActorId=adminId,TargetUserId=refund.Payment.Contract.Project.ClientId,Action="ReconcilePspRefund",Reason="Refund #"+id+": "+refund.Status,CreatedAtUtc=DateTime.UtcNow});
                db.SaveChanges();tx.Commit();
            }
        }
    }
    public static class VnPayRefundOutcome
    {
        // Caller has already authenticated the API response. Original payment success is never refund proof.
        public static string Classify(PspRefund refund,IDictionary<string,string> response,string command)
        {
            decimal amount;
            var code=VnPayCodec.Get(response,"vnp_ResponseCode");var status=VnPayCodec.Get(response,"vnp_TransactionStatus");
            if(VnPayCodec.Get(response,"vnp_Command")!=command || VnPayCodec.Get(response,"vnp_TxnRef")!=refund.Payment.TxnRef || VnPayCodec.Get(response,"vnp_TmnCode")!=refund.Payment.MerchantCode ||
                VnPayCodec.Get(response,"vnp_TransactionType")!=(refund.Amount==refund.Payment.Amount?"02":"03") ||
                !decimal.TryParse(VnPayCodec.Get(response,"vnp_Amount"),NumberStyles.None,CultureInfo.InvariantCulture,out amount) || amount!=refund.Amount*100 || code=="94")return "Unknown";
            var number=VnPayCodec.Get(response,"vnp_TransactionNo");
            if(code=="00" && status=="00" && Regex.IsMatch(number,"^[0-9]{1,15}$") && number.Any(c=>c!='0') && number!=refund.Payment.ProviderTransactionNo)return "Succeeded";
            if((status=="09" || status=="02") && Regex.IsMatch(code,"^[0-9]{2}$") && (command=="refund" || code=="00"))return "Failed";
            return "Unknown";
        }
    }
}
