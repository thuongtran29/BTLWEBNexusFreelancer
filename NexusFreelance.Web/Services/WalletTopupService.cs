using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Services
{
    public sealed class WalletTopupService
    {
        private readonly AppDbContext db;
        public WalletTopupService(AppDbContext db) { this.db = db; }
        private VnPaySettings Settings()
        {
            var settings = VnPaySettings.Load();
            if (settings.Environment == "production" && (string.Equals(db.Database.Connection.Database, "NexusFreelanceMvp", StringComparison.OrdinalIgnoreCase) || db.Database.Connection.Database.StartsWith("NexusFreelanceSubmission", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("CSDL phát triển có số dư minh họa, chưa được nhận tiền thật. Cần CSDL vận hành và merchant được VNPay chấp thuận.");
            return settings;
        }
        public WalletTopup Create(int userId, decimal amount)
        {
            if (!WalletService.ValidAmount(amount) || amount < 10000 || amount > 9999999999m) throw new InvalidOperationException("Số tiền nạp VNPay phải từ 10.000 đến 9.999.999.999 VND, không có phần lẻ.");
            var settings = Settings();
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                db.Database.SqlQuery<int>("SELECT Id FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", userId).Single();
                if (!db.Users.Any(u => u.Id == userId && u.Role == "Client" && !u.IsSuspended)) throw new InvalidOperationException("Chỉ hồ sơ khách hàng được nạp tiền.");
                var now = DateTime.UtcNow;
                var existing = db.WalletTopups.Where(t => t.UserAccountId == userId && t.Status == "Pending" && t.ExpiresAtUtc > now && t.Amount == amount && t.MerchantCode == settings.MerchantCode && t.Environment == settings.Environment).OrderByDescending(t => t.Id).FirstOrDefault();
                if (existing != null) { tx.Commit(); return existing; }
                if (db.WalletTopups.Count(t => t.UserAccountId == userId && t.Status == "Pending" && t.ExpiresAtUtc > now) >= 5) throw new InvalidOperationException("Bạn có 5 lệnh nạp còn hiệu lực; hãy xử lý các lệnh hiện có.");
                var order = new WalletTopup { UserAccountId = userId, Amount = amount, TxnRef = "W" + Guid.NewGuid().ToString("N"), Status = "Pending", MerchantCode = settings.MerchantCode, Environment = settings.Environment, CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(15) };
                db.WalletTopups.Add(order); db.SaveChanges(); tx.Commit(); return order;
            }
        }
        public string CheckoutUrl(WalletTopup order, string ip)
        {
            var settings = Settings();
            if (order.Status != "Pending" || order.ExpiresAtUtc <= DateTime.UtcNow || order.MerchantCode != settings.MerchantCode || order.Environment != settings.Environment) throw new InvalidOperationException("Lệnh nạp đã hết hạn, đã xử lý hoặc khác merchant. Kiểm tra lịch sử trước khi tạo lệnh mới.");
            return VnPayCodec.CheckoutTopup(order, settings, ip);
        }
        // Shared lock with escrow captures: the same PSP transaction cannot fund two orders.
        public static void LockProviderTransaction(AppDbContext db, VnPaySettings settings, string transaction)
        {
            if (string.IsNullOrEmpty(transaction) || !Regex.IsMatch(transaction, "^[0-9]{1,15}$")) return;
            var resource = "Nexus.VNPay:" + settings.Environment + ":" + settings.MerchantCode + ":" + transaction;
            var code = db.Database.SqlQuery<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@p0,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000; SELECT @r", resource).Single();
            if (code < 0) throw new InvalidOperationException("Giao dịch PSP đang được đối soát đồng thời.");
        }
        public string ReceiveIpn(IDictionary<string, string> fields)
        {
            var settings = Settings();
            return VnPayCodec.VerifyQuery(fields, settings) ? Record(fields, settings) : "97";
        }
        private string Record(IDictionary<string, string> fields, VnPaySettings settings)
        {
            var reference = VnPayCodec.Get(fields, "vnp_TxnRef");
            var code = VnPayCodec.Get(fields, "vnp_ResponseCode");
            var status = VnPayCodec.Get(fields, "vnp_TransactionStatus");
            if (!Regex.IsMatch(reference, "^W[a-f0-9]{32}$") || !Regex.IsMatch(code, "^[0-9]{2}$") || !Regex.IsMatch(status, "^[0-9]{2}$") || VnPayCodec.Get(fields, "vnp_TmnCode") != settings.MerchantCode) return "97";
            var owner = db.WalletTopups.Where(t => t.TxnRef == reference).Select(t => (int?)t.UserAccountId).SingleOrDefault();
            if (!owner.HasValue) return "01";
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var transaction = VnPayCodec.Get(fields, "vnp_TransactionNo");
                LockProviderTransaction(db, settings, transaction);
                db.Database.SqlQuery<int>("SELECT Id FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", owner.Value).Single();
                var order = db.WalletTopups.Single(t => t.TxnRef == reference); db.Entry(order).Reload();
                if (order.Environment != settings.Environment || order.MerchantCode != settings.MerchantCode) return "97";
                decimal amount;
                var currency = VnPayCodec.Get(fields, "vnp_CurrCode");
                if (!decimal.TryParse(VnPayCodec.Get(fields, "vnp_Amount"), NumberStyles.None, CultureInfo.InvariantCulture, out amount) || amount != order.Amount * 100 || (currency != "" && currency != "VND")) return "04";
                if (order.Status == "Paid") return "02";
                if (code == "00" && status == "00")
                {
                    if (!Regex.IsMatch(transaction, "^[0-9]{1,15}$") || transaction.All(c => c == '0')) return "97";
                    if (db.WalletTopups.Any(t => t.Id != order.Id && t.Environment == settings.Environment && t.MerchantCode == settings.MerchantCode && t.ProviderTransactionNo == transaction) || db.PaymentIntents.Any(p => p.Environment == settings.Environment && p.MerchantCode == settings.MerchantCode && p.ProviderTransactionNo == transaction)) return "97";
                    var wallet = db.WalletAccounts.SingleOrDefault(w => w.UserAccountId == owner.Value);
                    if (wallet == null) { wallet = new WalletAccount { UserAccountId = owner.Value }; db.WalletAccounts.Add(wallet); } else db.Entry(wallet).Reload();
                    wallet.Balance += order.Amount;
                    db.WalletEntries.Add(new WalletEntry { UserAccountId = owner.Value, Kind = "Deposit", Amount = order.Amount, BalanceDelta = order.Amount, HeldDelta = 0, Reference = "vnpay-topup:" + order.Id, CreatedAtUtc = DateTime.UtcNow });
                    order.Status = "Paid"; order.ProviderTransactionNo = transaction; order.CapturedAtUtc = DateTime.UtcNow;
                    NotificationService.Queue(db, owner.Value, "VNPay xác nhận nạp " + order.Amount.ToString("N0") + " VND vào ví", "/Wallet");
                }
                else order.Status = code == "00" && status == "01" ? "Pending" : "Failed";
                db.SaveChanges(); tx.Commit(); return "00";
            }
        }
        public async Task Reconcile(int id, int actorId, string ip)
        {
            var order = db.WalletTopups.AsNoTracking().SingleOrDefault(t => t.Id == id && t.UserAccountId == actorId);
            if (order == null) throw new InvalidOperationException("Không tìm thấy lệnh nạp thuộc tài khoản.");
            var settings = Settings();
            var response = await new VnPayGateway(settings).Send(new PaymentIntent { TxnRef = order.TxnRef, Amount = order.Amount, Environment = order.Environment, MerchantCode = order.MerchantCode, TransactionDate = VnPayCodec.VietnamDate(order.CreatedAtUtc), ProviderTransactionNo = order.ProviderTransactionNo }, null, ip);
            if (VnPayCodec.Get(response, "vnp_Command") != "querydr" || VnPayCodec.Get(response, "vnp_ResponseCode") != "00" || VnPayCodec.Get(response, "vnp_TransactionType") != "01" || VnPayCodec.Get(response, "vnp_TxnRef") != order.TxnRef) throw new InvalidOperationException("Chưa có kết quả đối soát xác định từ VNPay.");
            var result = Record(response, settings);
            if (result != "00" && result != "02") throw new InvalidOperationException("Kết quả đối soát chưa khớp lệnh nạp.");
        }
    }
}
