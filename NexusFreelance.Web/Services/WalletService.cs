using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Services
{
    public sealed class WalletService
    {
        private readonly AppDbContext db;
        public WalletService(AppDbContext db) { this.db = db; }
        private static void Must(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        public static bool ValidAmount(decimal amount) => amount > 0 && amount <= 999999999999m && decimal.Truncate(amount) == amount;

        private Project LockProject(int id)
        {
            var p = db.Projects.SqlQuery("SELECT * FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", id).SingleOrDefault();
            Must(p != null, "Không tìm thấy dự án."); db.Entry(p).Reload(); return p;
        }
        private Dictionary<int,WalletAccount> LockWallets(IEnumerable<int> ids)
        {
            var result = new Dictionary<int,WalletAccount>();
            foreach (var id in ids.Distinct().OrderBy(i => i))
            {
                db.Database.SqlQuery<int>("SELECT Id FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", id).Single();
                var wallet = db.WalletAccounts.SingleOrDefault(w => w.UserAccountId == id);
                if (wallet == null) { wallet = new WalletAccount { UserAccountId = id }; db.WalletAccounts.Add(wallet); }
                else db.Entry(wallet).Reload();
                result[id] = wallet;
            }
            return result;
        }
        private void Entry(WalletAccount wallet, string kind, decimal amount, decimal balance, decimal held, string reference, int? milestone = null)
        {
            wallet.Balance += balance; wallet.Held += held;
            Must(wallet.Balance >= 0 && wallet.Held >= 0, "Số dư không đủ.");
            db.WalletEntries.Add(new WalletEntry { UserAccountId = wallet.UserAccountId, Kind = kind, Amount = amount,
                BalanceDelta = balance, HeldDelta = held, Reference = reference, EscrowMilestoneId = milestone, CreatedAtUtc = DateTime.UtcNow });
        }
        private void Audit(int actor, int target, string action, string reason)
        {
            db.AdminAuditLogs.Add(new AdminAuditLog { ActorId = actor, TargetUserId = target, Action = action,
                Reason = reason.Substring(0, Math.Min(reason.Length, 200)), CreatedAtUtc = DateTime.UtcNow });
        }

        public void RequestMoney(int userId, string kind, decimal amount, string reference, string details)
        {
            Must(ValidAmount(amount), "Số tiền phải là số nguyên VND từ 1 đến 999.999.999.999.");
            Must(kind == "Deposit" || kind == "Withdrawal", "Loại yêu cầu không hợp lệ.");
            Must(!string.IsNullOrWhiteSpace(reference) && reference.Length <= 100 && !string.IsNullOrWhiteSpace(details) && details.Length <= 300, "Cần mã tham chiếu và nội dung hợp lệ.");
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var wallet = LockWallets(new[] { userId })[userId];
                var user = db.Users.Find(userId);
                Must((kind == "Deposit" && user.Role == "Client") || (kind == "Withdrawal" && user.Role == "Freelancer"), "Vai trò không phù hợp.");
                reference = reference.Trim();
                if (db.MoneyRequests.Any(r => r.Reference == reference)) throw new InvalidOperationException("Mã tham chiếu đã được dùng.");
                Must(db.MoneyRequests.Count(r => r.UserAccountId == userId && r.Status == "Pending") < 10, "Bạn đã có 10 yêu cầu đang chờ xử lý.");
                if (kind == "Withdrawal")
                {
                    var pending = db.MoneyRequests.Where(r => r.UserAccountId == userId && r.Kind == "Withdrawal" && r.Status == "Pending").Select(r => (decimal?)r.Amount).Sum() ?? 0m;
                    Must(wallet.Balance - pending >= amount, "Số dư khả dụng sau các yêu cầu rút đang chờ không đủ.");
                }
                db.MoneyRequests.Add(new MoneyRequest { UserAccountId = userId, Kind = kind, Amount = amount, Reference = reference.Trim(),
                    Details = details.Trim(), Status = "Pending", CreatedAtUtc = DateTime.UtcNow });
                NotificationService.QueueAdmins(db,"Có yêu cầu nạp / rút ví chờ đối soát","/Wallet/Admin");
                db.SaveChanges(); tx.Commit();
            }
        }
        public void ReviewMoney(int id, int adminId, bool approve, string reason)
        {
            Must(!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length >= 5 && reason.Length <= 300, "Nhập lý do / chứng từ ít nhất 5 ký tự.");
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                Must(db.Users.Any(u => u.Id == adminId && u.Role == "Admin" && !u.IsSuspended), "Chỉ quản trị được duyệt.");
                var targetId = db.MoneyRequests.Where(r => r.Id == id).Select(r => (int?)r.UserAccountId).SingleOrDefault();
                Must(targetId.HasValue, "Không tìm thấy yêu cầu."); Must(targetId.Value != adminId, "Không tự duyệt giao dịch của mình.");
                var wallet = LockWallets(new[] { targetId.Value })[targetId.Value];
                var request = db.MoneyRequests.Single(r => r.Id == id); db.Entry(request).Reload();
                Must(request.Status == "Pending", "Yêu cầu đã được xử lý.");
                if (approve) Entry(wallet, request.Kind, request.Amount, request.Kind == "Deposit" ? request.Amount : -request.Amount, 0, "money:" + id);
                request.Status = approve ? "Approved" : "Rejected"; request.ReviewerId = adminId; request.ReviewedAtUtc = DateTime.UtcNow; request.Resolution = reason.Trim();
                Audit(adminId, targetId.Value, "ReviewMoneyRequest", "Request #" + id + ": " + request.Status + ". " + reason.Trim());
                NotificationService.Queue(db, targetId.Value, "Yêu cầu ví #" + id + ": " + request.Status, "/Wallet");
                db.SaveChanges(); tx.Commit();
            }
        }

        // Compatibility entry point: each selected bid has its own contract and plan.
        public void CreateContract(Project project) => new ContractService(db).EnsureSelected(project);

        public void Plan(int projectId, int clientId, string lines, int? contractId = null)
        {
            Must(contractId.HasValue, "Chọn hợp đồng và đề nghị phiên bản điều khoản mới trước khi bảo đảm.");
            var c=db.Contracts.SingleOrDefault(x=>x.Id==contractId && x.ProjectId==projectId);
            Must(c!=null && c.Project.ClientId==clientId,"Không có quyền lập mốc hợp đồng này.");
            var v=new ContractService(db).Current(c);var snapshot=ContractService.ReadSnapshot(v);
            new ContractService(db).Revise(c.Id,clientId,snapshot.Scope,lines,v.DeliveryDays);
        }

        public void Fund(int milestoneId, int clientId)
        {
            var projectId = db.EscrowMilestones.Where(x => x.Id == milestoneId).Select(x => (int?)x.ProjectId).SingleOrDefault();
            Must(projectId.HasValue, "Mốc không tồn tại.");
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var project = LockProject(projectId.Value);
                Must(project.ClientId == clientId && project.Status != "Cancelled" && project.Status != "Completed", "Không thể ký quỹ dự án này.");
                var m = db.EscrowMilestones.Single(x => x.Id == milestoneId); db.Entry(m).Reload();
                if(m.ContractId.HasValue)
                {
                    new ContractService(db).RequireFundingReady(m.ContractId.Value);
                    Must(!db.PaymentIntents.Any(p=>p.EscrowMilestoneId==m.Id && p.Status=="Pending"),"Đang có thanh toán VNPay; hãy đối soát trước khi dùng ví.");
                }
                HoldUnderLock(project,m,clientId);
                db.SaveChanges(); tx.Commit();
            }
        }

        private void HoldUnderLock(Project project, EscrowMilestone m, int clientId)
        {
            Must(m.Status=="Planned","Mốc đã được ký quỹ hoặc xử lý.");
            var wallet=LockWallets(new[]{clientId})[clientId];
            var pending=db.MoneyRequests.Where(r=>r.UserAccountId==clientId && r.Kind=="Withdrawal" && r.Status=="Pending").Select(r=>(decimal?)r.Amount).Sum()??0;
            Must(wallet.Balance-pending>=m.Amount,"Số dư khả dụng không đủ, gồm khoản đang chờ rút.");
            Entry(wallet,"EscrowHold",m.Amount,-m.Amount,m.Amount,"hold:"+m.Id,m.Id);m.Status="Funded";
            if(m.ContractId.HasValue)
            {
                var c=db.Contracts.Single(x=>x.Id==m.ContractId.Value);
                var first=db.EscrowMilestones.Where(x=>x.ContractId==c.Id).OrderBy(x=>x.Id).Select(x=>x.Id).First();
                if(m.Id==first) c.Status="Active";
                RefreshProjectState(project);
            }
            else if(project.Status=="AwaitingEscrow") project.Status="InProgress";
            foreach(var recipient in db.EscrowShares.Where(x=>x.ProjectId==project.Id && x.ContractId==m.ContractId).Select(x=>x.UserAccountId).ToList())
                NotificationService.Queue(db,recipient,"Đã bảo đảm mốc #"+m.Id,"/Wallet/Project/"+project.Id);
        }
        public bool CaptureProviderUnderLock(PaymentIntent payment)
        {
            Must(db.Database.CurrentTransaction!=null,"Thiếu giao dịch nhận tiền PSP.");
            var c=db.Contracts.Include(x=>x.Project).Single(x=>x.Id==payment.ContractId);
            var m=db.EscrowMilestones.Single(x=>x.Id==payment.EscrowMilestoneId);
            // QueryDR may have awaited HTTP after the controller tracked these rows.
            // Caller holds the project lock: allocation must use current committed state.
            db.Entry(c).Reload();db.Entry(c.Project).Reload();db.Entry(m).Reload();
            Must(m.ContractId==c.Id && m.Amount==payment.Amount && payment.Currency=="VND","Giao dịch PSP không khớp nghĩa vụ thanh toán.");
            var wallet=LockWallets(new[]{c.Project.ClientId})[c.Project.ClientId];
            Entry(wallet,"PspCapture",payment.Amount,payment.Amount,0,"vnpay:capture:"+payment.Id,m.Id);db.SaveChanges();
            // A late payment for an already funded/closed obligation stays available for reconciliation/refund.
            if(m.Status!="Planned" || (c.Status!="AwaitingEscrow" && c.Status!="Active")) return false;
            try {new ContractService(db).RequireFundingReady(c.Id);} catch(InvalidOperationException){return false;}
            HoldUnderLock(c.Project,m,c.Project.ClientId);return true;
        }
        public void ReserveProviderRefundUnderLock(PaymentIntent payment,decimal amount,string key)
        {
            var clientId=db.Projects.Where(p=>p.Id==payment.Contract.ProjectId).Select(p=>p.ClientId).Single();
            var wallet=LockWallets(new[]{clientId})[clientId];
            var pending=db.MoneyRequests.Where(r=>r.UserAccountId==clientId && r.Kind=="Withdrawal" && r.Status=="Pending").Select(r=>(decimal?)r.Amount).Sum()??0;
            Must(wallet.Balance-pending>=amount,"Khoản hoàn chưa có số dư khả dụng; cần quyết toán tranh chấp trước.");
            Entry(wallet,"PspRefundReserve",amount,-amount,0,"vnpay:refund:"+key,payment.EscrowMilestoneId);
        }
        public void RestoreProviderRefundUnderLock(PspRefund refund)
        {
            var clientId=refund.Payment.Contract.Project.ClientId;var wallet=LockWallets(new[]{clientId})[clientId];
            Entry(wallet,"PspRefundRestore",refund.Amount,refund.Amount,0,"vnpay:refund-restore:"+refund.Id,refund.Payment.EscrowMilestoneId);
        }
        public void RefreshProjectState(Project project)
        {
            if(project.Status=="Published" || project.Status=="Paused" || project.Status=="Draft") return;
            db.SaveChanges();
            var states=db.Contracts.Where(c=>c.ProjectId==project.Id).Select(c=>c.Status).ToList();
            if(states.Count==0)return;
            if(states.All(x=>x=="Completed" || x=="Terminated" || x=="Declined")) project.Status=states.Any(x=>x=="Completed")?"Completed":"Cancelled";
            else if(states.Any(x=>x=="Active"))project.Status="InProgress";
            else if(states.Any(x=>x=="Disputed"))project.Status="Disputed";
            else project.Status="AwaitingEscrow";
        }

        public EscrowMilestone SubmissionMilestone(int projectId, int? milestoneId, bool revision, int? contractId = null)
        {
            var query = db.EscrowMilestones.Where(x => x.ProjectId == projectId && x.ContractId == contractId);
            if (!query.Any()) return null; // Historical pre-escrow contracts only.
            var m = milestoneId.HasValue ? query.SingleOrDefault(x => x.Id == milestoneId.Value) : query.Where(x => x.Status == "Funded" || x.Status == "RevisionsRequested").OrderBy(x => x.Id).FirstOrDefault();
            Must(m != null && (m.Status == "Funded" || m.Status == "RevisionsRequested"), "Chọn mốc đã ký quỹ hoặc đang yêu cầu sửa.");
            if (revision) Must(m.Status == "RevisionsRequested", "Mốc không đang yêu cầu sửa.");
            m.Status = "Submitted"; m.SubmittedAtUtc = DateTime.UtcNow; m.ReviewRemindedAtUtc = null; return m;
        }

        // Caller holds the project lock and owns the surrounding transaction.
        public void ReleaseUnderLock(int milestoneId, decimal refund = 0)
        {
            var m = db.EscrowMilestones.Include(x => x.Project).Single(x => x.Id == milestoneId);
            Must(m.Status == "Submitted" || m.Status == "Funded" || m.Status == "RevisionsRequested", "Mốc không thể giải ngân.");
            Must(refund >= 0 && refund <= m.Amount && refund == decimal.Truncate(refund), "Số hoàn tiền không hợp lệ.");
            if(m.ContractId.HasValue) Must(db.Contracts.Any(c=>c.Id==m.ContractId && c.Status=="Active"),"Hợp đồng không thể giải ngân khi chưa hoạt động hoặc đang tranh chấp.");
            var shares = db.EscrowShares.Where(s => s.ProjectId == m.ProjectId && s.ContractId == m.ContractId).OrderBy(s => s.UserAccountId).ToList();
            Must(shares.Count > 0 && shares.All(s => s.Weight > 0), "Chưa chốt phân chia thành viên.");
            var wallets = LockWallets(shares.Select(s => s.UserAccountId).Concat(new[] { m.Project.ClientId }));
            var client = wallets[m.Project.ClientId];
            Entry(client, "EscrowSettle", m.Amount, 0, -m.Amount, "settle:" + m.Id, m.Id);
            if (refund > 0) Entry(client, "Refund", refund, refund, 0, "refund:" + m.Id, m.Id);
            var rate=m.ContractId.HasValue?new ContractService(db).Current(db.Contracts.Single(c=>c.Id==m.ContractId)).FeeRate:m.Project.PlatformFeeRate;
            var gross = m.Amount - refund; var fee = decimal.Floor(gross * rate); var net = gross - fee;
            // PlatformFee is recorded once, with no user balance credit.
            if (fee > 0) Entry(client, "PlatformFee", fee, 0, 0, "fee:" + m.Id, m.Id);
            var remaining = net; var weight = shares.Sum(s => s.Weight);
            for (var i = 0; i < shares.Count; i++)
            {
                var share = shares[i]; var amount = i == shares.Count - 1 ? remaining : decimal.Floor(net * share.Weight / weight); remaining -= amount;
                if (amount > 0) Entry(wallets[share.UserAccountId], "Payout", amount, amount, 0, "payout:" + m.Id + ":" + share.UserAccountId, m.Id);
                NotificationService.Queue(db, share.UserAccountId, "Mốc đã quyết toán: " + amount.ToString("N0") + " VND", "/Wallet");
            }
            m.Fee = fee; m.Refunded = refund; m.Status = refund == m.Amount ? "Refunded" : "Released"; m.SettledAtUtc = DateTime.UtcNow;
            db.SaveChanges();
            if(m.ContractId.HasValue)
            {
                if(!db.EscrowMilestones.Any(x=>x.ContractId==m.ContractId && x.Status!="Released" && x.Status!="Refunded"))
                    db.Contracts.Single(c=>c.Id==m.ContractId).Status=db.EscrowMilestones.Any(x=>x.ContractId==m.ContractId && x.Status=="Refunded")?"Terminated":"Completed";
                RefreshProjectState(m.Project);
            }
            else if(!db.EscrowMilestones.Any(x=>x.ProjectId==m.ProjectId && x.Status!="Released" && x.Status!="Refunded"))
                m.Project.Status=db.EscrowMilestones.Any(x=>x.ProjectId==m.ProjectId && x.Status=="Refunded")?"Cancelled":"Completed";
        }

        public void Dispute(int projectId, int reporterId, string reason, int? contractId = null)
        {
            Must(!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length >= 15 && reason.Length <= 2000, "Mô tả khiếu nại từ 15 đến 2000 ký tự.");
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var p = LockProject(projectId);
                if(db.Contracts.Any(c=>c.ProjectId==projectId))
                {
                    Must(contractId.HasValue,"Chọn đúng hợp đồng cần khiếu nại.");
                    var c=db.Contracts.SingleOrDefault(x=>x.Id==contractId && x.ProjectId==projectId);
                    Must(c!=null && c.Status=="Active" && new ContractService(db).CanView(c,reporterId),"Không có quyền khiếu nại hợp đồng này.");
                    c.Status="Disputed";
                }
                else Must(p.Status=="InProgress" && (p.ClientId==reporterId || db.EscrowShares.Any(s=>s.ProjectId==projectId && s.UserAccountId==reporterId)),"Không có quyền khiếu nại.");
                Must(db.EscrowMilestones.Any(m => m.ProjectId == projectId && m.ContractId == contractId && (m.Status == "Funded" || m.Status == "Submitted" || m.Status == "RevisionsRequested")), "Không có mốc ký quỹ để xử lý tranh chấp.");
                if(!contractId.HasValue) p.Status = "Disputed"; db.EscrowDisputes.Add(new EscrowDispute { ContractId=contractId, ProjectId = projectId, ReporterId = reporterId, Reason = reason.Trim(), Status = "Pending", CreatedAtUtc = DateTime.UtcNow });
                NotificationService.QueueAdmins(db,"Có khiếu nại ký quỹ cần xử lý","/Wallet/Admin");
                NotificationService.Queue(db, p.ClientId, contractId.HasValue?"Hợp đồng #"+contractId+" có khiếu nại, phần ký quỹ đang được giữ":"Dự án có khiếu nại, ký quỹ đang đóng băng", contractId.HasValue?"/Contracts/Details/"+contractId:"/Wallet/Project/" + projectId);
                db.SaveChanges(); tx.Commit();
            }
        }
        public void Resolve(int id, int adminId, decimal refund, string reason)
        {
            Must(reason != null && reason.Trim().Length >= 15 && reason.Length <= 1000, "Kết luận cần từ 15 đến 1000 ký tự.");
            var projectId = db.EscrowDisputes.Where(d => d.Id == id).Select(d => (int?)d.ProjectId).SingleOrDefault();
            Must(projectId.HasValue, "Không tìm thấy khiếu nại.");
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                Must(db.Users.Any(u => u.Id == adminId && u.Role == "Admin" && !u.IsSuspended), "Chỉ quản trị được quyết định.");
                var p = LockProject(projectId.Value); var d = db.EscrowDisputes.Single(x => x.Id == id); db.Entry(d).Reload();
                Must(d.Status == "Pending" && (d.ContractId.HasValue ? db.Contracts.Any(c=>c.Id==d.ContractId && c.Status=="Disputed") : p.Status == "Disputed"), "Khiếu nại đã được xử lý.");
                var milestones = db.EscrowMilestones.Where(m => m.ProjectId == p.Id && m.ContractId==d.ContractId && (m.Status == "Funded" || m.Status == "Submitted" || m.Status == "RevisionsRequested")).OrderBy(m => m.Id).ToList();
                Must(refund >= 0 && refund <= milestones.Sum(m => m.Amount) && refund == decimal.Truncate(refund), "Số tiền hoàn không hợp lệ.");
                var remaining = refund; if(d.ContractId.HasValue) db.Contracts.Single(c=>c.Id==d.ContractId).Status="Active"; else p.Status = "InProgress";
                db.SaveChanges();
                foreach (var m in milestones) { var part = Math.Min(remaining, m.Amount); remaining -= part; ReleaseUnderLock(m.Id, part); }
                NotificationService.Queue(db, p.ClientId, "Đã có kết luận tranh chấp hợp đồng của " + p.Title.Substring(0, Math.Min(100, p.Title.Length)), d.ContractId.HasValue ? "/Contracts/Details/" + d.ContractId : "/Wallet/Project/" + p.Id, adminId, p.Id);
                var recipients = d.ContractId.HasValue ? db.ContractMembers.Where(m => m.ContractId == d.ContractId).Select(m => m.UserAccountId).ToList() : db.TeamMembers.Where(m => m.Team.ProjectId == p.Id).Select(m => m.UserAccountId).ToList();
                foreach(var recipient in recipients.Distinct().Where(u => u != p.ClientId)) NotificationService.Queue(db, recipient, "Đã có kết luận xử lý tranh chấp", d.ContractId.HasValue ? "/Contracts/Details/" + d.ContractId : "/Wallet/Project/" + p.Id, adminId, p.Id);
                d.Status = "Resolved"; d.Resolution = reason.Trim(); d.ReviewerId = adminId; d.ReviewedAtUtc = DateTime.UtcNow;
                Audit(adminId, p.ClientId, "ResolveEscrowDispute", "Dispute #" + id + ": refund " + refund + ". " + reason.Trim());
                db.SaveChanges(); tx.Commit();
            }
        }
    }
}
