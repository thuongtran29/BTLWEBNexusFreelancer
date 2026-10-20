using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using NexusFreelance.Web.Services;
namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public sealed class PaymentsController : BaseController
    {
        private PspPaymentService Payments => new PspPaymentService(Db);
        [HttpPost,ValidateAntiForgeryToken]
        public ActionResult Checkout(int id)
        {
            var owner=Db.EscrowMilestones.Where(m=>m.Id==id).Select(m=>(int?)m.Project.ClientId).SingleOrDefault();if(!owner.HasValue)return HttpNotFound();
            if(CurrentUser.Role!="Client" || owner.Value!=CurrentUser.Id)return Forbidden();
            try{return Redirect(Payments.Checkout(id,CurrentUser.Id,Request.UserHostAddress).CheckoutUrl);}
            catch(InvalidOperationException ex){return new HttpStatusCodeResult(409,ex.Message);}
            catch(System.Data.Entity.Infrastructure.DbUpdateException){return new HttpStatusCodeResult(409,"Yêu cầu đã được xử lý; hãy đối soát giao dịch hiện tại.");}
        }
        [AllowAnonymous,HttpGet]
        public ActionResult Ipn()
        {
            string result;
            try { var fields=VnPayCodec.ParseQuery(Request.QueryString); result=VnPayCodec.Get(fields,"vnp_TxnRef").StartsWith("W",StringComparison.Ordinal) ? new WalletTopupService(Db).ReceiveIpn(fields) : Payments.ReceiveIpn(fields); }
            catch(InvalidOperationException){result="97";}
            catch(System.Data.Entity.Infrastructure.DbUpdateException){result="99";}
            catch(System.Data.SqlClient.SqlException){result="99";}
            var message=result=="00"?"Confirm Success":result=="02"?"Order already confirmed":result=="01"?"Order not found":result=="04"?"Invalid amount or currency":result=="97"?"Invalid signature or parameters":"Retry later";
            return Json(new {RspCode=result,Message=message},JsonRequestBehavior.AllowGet);
        }
        [AllowAnonymous,HttpGet]
        public ActionResult Return()
        {
            try {ViewBag.Verified=VnPayCodec.VerifyQuery(VnPayCodec.ParseQuery(Request.QueryString),VnPaySettings.Load());}
            catch(InvalidOperationException){ViewBag.Verified=false;}
            // Deliberately no payment-state or ledger mutation on a browser return.
            ViewBag.IsTopup = (Request.QueryString["vnp_TxnRef"] ?? "").StartsWith("W", StringComparison.Ordinal);
            return View();
        }
        [HttpPost,ValidateAntiForgeryToken]
        public async Task<ActionResult> Reconcile(int id)
        {
            var p=Db.PaymentIntents.Include(x=>x.Contract.Project).SingleOrDefault(x=>x.Id==id);if(p==null)return HttpNotFound();
            if(CurrentUser.Role!="Admin" && p.Contract.Project.ClientId!=CurrentUser.Id)return Forbidden();
            try {await Payments.Reconcile(id,CurrentUser.Id,Request.UserHostAddress);TempData["Notice"]="Đã đối soát kết quả có chữ ký từ VNPay.";}
            catch(Exception ex) when(ex is InvalidOperationException || ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException || ex is ArgumentException || ex is System.IO.IOException)
            {TempData["Error"]="Chưa đối soát được với VNPay. Tiền không được ghi nhận thêm; hãy kiểm tra lại khi PSP trả kết quả.";}
            return RedirectToAction("Details","Contracts",new{id=p.ContractId});
        }
        [HttpPost,ValidateAntiForgeryToken]
        public async Task<ActionResult> Refund(int id,decimal amount,string reason)
        {
            if(CurrentUser.Role!="Admin")return Forbidden();if(!ModelState.IsValid)return new HttpStatusCodeResult(400);
            var p=Db.PaymentIntents.SingleOrDefault(x=>x.Id==id);if(p==null)return HttpNotFound();
            try
            {
                var refundId=Payments.QueueRefund(id,CurrentUser.Id,amount,reason);
                await Payments.ProcessRefund(refundId,Request.UserHostAddress);
                TempData["Notice"]="Đã ghi nhận yêu cầu hoàn. Xem trạng thái PSP; khoản chưa rõ kết quả tiếp tục được giữ để đối soát.";
            }
            catch(InvalidOperationException ex){TempData["Error"]=ex.Message;}
            catch(System.Data.Entity.Infrastructure.DbUpdateException){TempData["Error"]="Yêu cầu đã được ghi nhận. Không gửi thêm lệnh trước khi đối soát.";}
            return RedirectToAction("Details","Contracts",new{id=p.ContractId});
        }
        [HttpPost,ValidateAntiForgeryToken]
        public async Task<ActionResult> ReconcileRefund(int id)
        {
            return await RefundOperation(id,false);
        }
        [HttpPost,ValidateAntiForgeryToken]
        public async Task<ActionResult> ProcessQueuedRefund(int id)
        {
            return await RefundOperation(id,true);
        }
        private async Task<ActionResult> RefundOperation(int id,bool queued)
        {
            if(CurrentUser.Role!="Admin")return Forbidden();
            var refund=Db.PspRefunds.Include(r=>r.Payment).SingleOrDefault(r=>r.Id==id);if(refund==null)return HttpNotFound();
            try
            {
                if(queued)await Payments.ProcessRefund(id,Request.UserHostAddress);
                else await Payments.ReconcileRefund(id,CurrentUser.Id,Request.UserHostAddress);
                TempData["Notice"]="Đã kiểm tra yêu cầu hoàn. Chỉ kết quả hoàn có chữ ký và khớp giao dịch mới được quyết toán.";
            }
            catch(Exception ex) when(ex is InvalidOperationException || ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException || ex is ArgumentException || ex is System.IO.IOException)
            {TempData["Error"]="Chưa có kết quả hoàn tiền xác định từ VNPay. Khoản dự phòng được giữ để tiếp tục đối soát.";}
            catch(System.Data.Entity.Infrastructure.DbUpdateException){TempData["Error"]="Trạng thái đã thay đổi; hãy tải lại lịch sử hoàn tiền.";}
            return RedirectToAction("Details","Contracts",new{id=refund.Payment.ContractId});
        }
    }
}
