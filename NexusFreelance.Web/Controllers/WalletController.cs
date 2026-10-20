using System;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Services;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public sealed class WalletController : BaseController
    {
        private WalletService Money => new WalletService(Db);
        [HttpGet]
        public ActionResult Index(int? transferId = null)
        {
            if (CurrentUser.Role == "Admin") return RedirectToAction("Admin");
            ViewBag.BankTopup = transferId.HasValue && CurrentUser.Role == "Client"
                ? new WalletBankTransferService(Db).Find(CurrentUser.Id, transferId.Value) : null;
            if (transferId.HasValue && ViewBag.BankTopup == null) return HttpNotFound();
            ViewBag.Wallet = Db.WalletAccounts.SingleOrDefault(w => w.UserAccountId == CurrentUser.Id);
            ViewBag.PendingWithdrawalAmount = Db.MoneyRequests.Where(r => r.UserAccountId == CurrentUser.Id && r.Kind == "Withdrawal" && r.Status == "Pending")
                .Select(r => (decimal?)r.Amount).Sum() ?? 0m;
            ViewBag.Topups = Db.WalletTopups.Where(t => t.UserAccountId == CurrentUser.Id).OrderByDescending(t => t.Id).Take(30).ToList();
            ViewBag.VnPayReady = VnPaySettings.IsConfigured;
            ViewBag.Requests = Db.MoneyRequests.Where(r => r.UserAccountId == CurrentUser.Id).OrderByDescending(r => r.Id).Take(50).ToList();
            ViewBag.PendingRequests = Db.MoneyRequests.Count(r => r.UserAccountId == CurrentUser.Id && r.Status == "Pending");
            ViewBag.Entries = Db.WalletEntries.Where(e => e.UserAccountId == CurrentUser.Id).OrderByDescending(e => e.Id).Take(100).ToList();
            ViewBag.PspRefundReserved=Db.PspRefunds.Where(r=>r.Payment.Contract.Project.ClientId==CurrentUser.Id && (r.Status=="Queued" || r.Status=="Processing" || r.Status=="Unknown")).Select(r=>(decimal?)r.Amount).Sum()??0m;
            ViewBag.EscrowProjects = Db.Projects.Where(p => p.ClientId == CurrentUser.Id || Db.EscrowShares.Any(s => s.ProjectId == p.Id && s.UserAccountId == CurrentUser.Id))
                .Where(p => Db.EscrowMilestones.Any(m => m.ProjectId == p.Id)).OrderByDescending(p => p.Id).Take(50).ToList();
            return View();
        }
        [HttpGet]
        public ActionResult Project(int id)
        {
            var p = Db.Projects.Find(id); if (p == null) return HttpNotFound();
            if (CurrentUser.Role != "Admin" && p.ClientId != CurrentUser.Id && !Db.EscrowShares.Any(s => s.ProjectId == id && s.UserAccountId == CurrentUser.Id)) return Forbidden();
            if(Db.Contracts.Any(c=>c.ProjectId==id))return RedirectToAction("Index","Contracts",new{projectId=id});
            ViewBag.IsOwner = p.ClientId == CurrentUser.Id;
            ViewBag.Milestones = Db.EscrowMilestones.Where(m => m.ProjectId == id).OrderBy(m => m.Id).ToList();
            ViewBag.Shares = Db.EscrowShares.Include(s => s.User).Where(s => s.ProjectId == id).OrderBy(s => s.UserAccountId).ToList();
            ViewBag.Disputes = Db.EscrowDisputes.Where(d => d.ProjectId == id).OrderByDescending(d => d.Id).Take(20).ToList();
            ViewBag.WorkspaceId = Db.Workspaces.Where(w => w.ProjectId == id).Select(w => (int?)w.Id).FirstOrDefault();
            return View(p);
        }
        [HttpGet]
        public ActionResult Admin()
        {
            if (CurrentUser.Role != "Admin") return Forbidden();
            ViewBag.Requests = Db.MoneyRequests.Include(r => r.User).Where(r => r.Status == "Pending").OrderBy(r => r.Id).Take(100).ToList();
            ViewBag.Disputes = Db.EscrowDisputes.Include(d => d.Project).Where(d => d.Status == "Pending").OrderBy(d => d.Id).Take(100).ToList();
            return View();
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Topup(decimal amount)
        {
            if(CurrentUser?.Role != "Client") return Forbidden();
            if(!ModelState.IsValid) { TempData["Error"]="Nhập số tiền VND hợp lệ."; return RedirectToAction("Index"); }
            try {
                if (!VnPaySettings.IsConfigured) {
                    var transfer = new WalletBankTransferService(Db).Create(CurrentUser.Id, amount);
                    return Redirect(Url.Action("Index", new { transferId = transfer.Id }) + "#topup-qr");
                }
                var service=new WalletTopupService(Db); var order=service.Create(CurrentUser.Id, amount);
                return Redirect(service.CheckoutUrl(order, Request.UserHostAddress));
            }
            catch(InvalidOperationException ex) { TempData["Error"]=ex.Message; }
            catch(System.Data.Entity.Infrastructure.DbUpdateException) { TempData["Error"]="Lệnh nạp vừa được cập nhật. Hãy kiểm tra lịch sử trước khi thử lại."; }
            catch(System.Data.SqlClient.SqlException) { TempData["Error"]="Lệnh nạp đang được xử lý đồng thời. Hãy kiểm tra lịch sử trước khi thử lại."; }
            return RedirectToAction("Index");
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult ContinueTopup(int id)
        {
            if(CurrentUser?.Role != "Client") return Forbidden();
            var order=Db.WalletTopups.SingleOrDefault(t => t.Id == id && t.UserAccountId == CurrentUser.Id);
            if(order == null) return HttpNotFound();
            try { return Redirect(new WalletTopupService(Db).CheckoutUrl(order, Request.UserHostAddress)); }
            catch(InvalidOperationException ex) { TempData["Error"]=ex.Message; return RedirectToAction("Index"); }
        }
        [HttpPost, ValidateAntiForgeryToken]
        public async System.Threading.Tasks.Task<ActionResult> ReconcileTopup(int id)
        {
            if(CurrentUser?.Role != "Client") return Forbidden();
            try { await new WalletTopupService(Db).Reconcile(id, CurrentUser.Id, Request.UserHostAddress); TempData["Notice"]="Đã đối soát lệnh nạp với VNPay. Xem số dư và lịch sử giao dịch."; }
            catch(Exception ex) when(ex is InvalidOperationException || ex is System.Net.Http.HttpRequestException || ex is System.Threading.Tasks.TaskCanceledException || ex is System.IO.IOException) { TempData["Error"]="Chưa đối soát được lệnh nạp. Số dư chưa được ghi thêm; hãy kiểm tra lại trước khi thanh toán lần nữa."; }
            catch(System.Data.Entity.Infrastructure.DbUpdateException) { TempData["Error"]="Lệnh nạp đã cập nhật đồng thời. Hãy tải lại để xem kết quả."; }
            return RedirectToAction("Index");
        }

        [HttpGet]
        public ActionResult ReviewDispute(int id)
        {
            if(CurrentUser?.Role != "Admin") return Forbidden();
            var dispute=Db.EscrowDisputes.Include(d => d.Project).Include(d => d.Reporter).SingleOrDefault(d => d.Id == id);
            if(dispute == null) return HttpNotFound();
            ViewBag.Milestones=Db.EscrowMilestones.Where(m => m.ProjectId == dispute.ProjectId && m.ContractId == dispute.ContractId).OrderBy(m => m.Id).ToList();
            ViewBag.Workspaces=Db.Workspaces.Where(w => w.ProjectId == dispute.ProjectId && w.ContractId == dispute.ContractId).ToList();
            return View(dispute);
        }

        private ActionResult Execute(Action command, string action, object route = null)
        {
            try { command(); TempData["Notice"] = "Đã lưu giao dịch / thay đổi trạng thái."; }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            catch (System.Data.Entity.Infrastructure.DbUpdateException) { TempData["Error"] = "Giao dịch đã xử lý hoặc dữ liệu thay đổi. Hãy kiểm tra sổ giao dịch trước khi thử lại."; }
            catch (System.Data.SqlClient.SqlException) { TempData["Error"] = "Giao dịch đang được xử lý đồng thời. Hãy tải lại để kiểm tra kết quả."; }
            return RedirectToAction(action, route);
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult RequestMoney(string kind, decimal amount, string reference, string details)
        {
            if (CurrentUser?.Role != "Freelancer" || kind != "Withdrawal") return Forbidden();
            if (!ModelState.IsValid) return new HttpStatusCodeResult(400);
            return Execute(() => Money.RequestMoney(CurrentUser.Id, "Withdrawal", amount, reference, details), "Index");
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult ReviewMoney(int id, bool approve, string reason)
        { if (CurrentUser.Role != "Admin") return Forbidden(); return Execute(() => Money.ReviewMoney(id, CurrentUser.Id, approve, reason), "Admin"); }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Fund(int id)
        {
            var project = Db.EscrowMilestones.Where(m => m.Id == id).Select(m => (int?)m.ProjectId).FirstOrDefault();
            if (!project.HasValue) return HttpNotFound();
            return Execute(() => Money.Fund(id, CurrentUser.Id), "Project", new { id = project });
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Plan(int id, string lines,int? contractId) => Execute(() => Money.Plan(id, CurrentUser.Id, lines,contractId), "Project", new { id });
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Dispute(int id, string reason,int? contractId) => Execute(() => Money.Dispute(id, CurrentUser.Id, reason,contractId), "Project", new { id });
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Resolve(int id, decimal refund, string reason)
        { if (CurrentUser.Role != "Admin") return Forbidden(); if (!ModelState.IsValid) return new HttpStatusCodeResult(400); return Execute(() => Money.Resolve(id, CurrentUser.Id, refund, reason), "Admin"); }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult ProcessDue()
        { if (CurrentUser.Role != "Admin") return Forbidden(); return Execute(() => FinanceSettlementRunner.Tick(), "Admin"); }
    }
}
