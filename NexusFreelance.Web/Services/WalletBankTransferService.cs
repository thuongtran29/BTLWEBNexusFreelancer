using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Services
{
    // Bank transfers use the existing manual-review ledger path, never PSP capture callbacks.
    public sealed class WalletBankTransferService
    {
        public const string BankName = "MB";
        public const string AccountNumber = "290820061111";
        public const string TransferDetails = "Nạp ví bằng QR chuyển khoản · MB · 290820061111. Chỉ duyệt sau khi đối chiếu tiền thực nhận và nội dung chuyển khoản.";
        private readonly AppDbContext db;
        public WalletBankTransferService(AppDbContext db) { this.db = db; }

        public static bool IsBankTopup(MoneyRequest request)
        {
            return request != null && request.Kind == "Deposit" && request.Details == TransferDetails &&
                Regex.IsMatch(request.Reference ?? "", "^NX[0-9A-F]{20}$");
        }

        public MoneyRequest Find(int userId, int id)
        {
            var request = db.MoneyRequests.SingleOrDefault(r => r.Id == id && r.UserAccountId == userId && r.Kind == "Deposit");
            return IsBankTopup(request) ? request : null;
        }

        public MoneyRequest Create(int userId, decimal amount)
        {
            if (!WalletService.ValidAmount(amount) || amount < 10000 || amount > 9999999999m)
                throw new InvalidOperationException("Số tiền nạp phải từ 10.000 đến 9.999.999.999 VND, không có phần lẻ.");
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                // Same user lock as review/capture: repeated submissions cannot create duplicate requests.
                var owner = db.Database.SqlQuery<int>("SELECT Id FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", userId).SingleOrDefault();
                if (owner == 0 || !db.Users.Any(u => u.Id == userId && u.Role == "Client" && !u.IsSuspended))
                    throw new InvalidOperationException("Chỉ hồ sơ khách hàng đang hoạt động được nạp tiền.");
                var existing = db.MoneyRequests.Where(r => r.UserAccountId == userId && r.Kind == "Deposit" && r.Status == "Pending" && r.Amount == amount && r.Details == TransferDetails)
                    .OrderByDescending(r => r.Id).ToList().FirstOrDefault(IsBankTopup);
                if (existing != null) { tx.Commit(); return existing; }
                if (db.MoneyRequests.Count(r => r.UserAccountId == userId && r.Status == "Pending") >= 10)
                    throw new InvalidOperationException("Bạn đã có 10 yêu cầu đang chờ. Hãy kiểm tra lịch sử hoặc liên hệ quản trị để đối soát trước khi tạo thêm.");
                var request = new MoneyRequest {
                    UserAccountId = userId, Kind = "Deposit", Status = "Pending", Amount = amount,
                    Reference = "NX" + Guid.NewGuid().ToString("N").Substring(0, 20).ToUpperInvariant(),
                    Details = TransferDetails, CreatedAtUtc = DateTime.UtcNow
                };
                db.MoneyRequests.Add(request);
                NotificationService.QueueAdmins(db, "Có lệnh nạp ví QR chờ đối soát ngân hàng", "/Wallet/Admin");
                db.SaveChanges(); tx.Commit(); return request;
            }
        }

        public static string QrPayload(MoneyRequest request)
        {
            if (!IsBankTopup(request) || request.Status != "Pending" || !WalletService.ValidAmount(request.Amount) || request.Amount < 10000 || request.Amount > 9999999999m)
                throw new InvalidOperationException("Lệnh nạp không còn chờ chuyển khoản / đối soát.");
            // NAPAS account-transfer TLV (QRIBFTTA), VND 704. MB BIN verified against
            // https://api.vietqr.io/v2/banks. This payload and its QR never leave our app.
            var beneficiary = Field("00", "970422") + Field("01", AccountNumber);
            var merchant = Field("00", "A000000727") + Field("01", beneficiary) + Field("02", "QRIBFTTA");
            var payload = Field("00", "01") + Field("01", "12") + Field("38", merchant) + Field("53", "704") +
                Field("54", request.Amount.ToString("0", CultureInfo.InvariantCulture)) + Field("58", "VN") +
                Field("62", Field("08", request.Reference)) + "6304";
            // CRC-16/CCITT-FALSE over the ASCII payload including the CRC tag and length.
            var crc = 0xFFFF;
            foreach (var character in payload) {
                crc ^= character << 8;
                for (var bit = 0; bit < 8; bit++) crc = ((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1) & 0xFFFF;
            }
            return payload + crc.ToString("X4", CultureInfo.InvariantCulture);
        }
        private static string Field(string id, string value) { return id + value.Length.ToString("D2", CultureInfo.InvariantCulture) + value; }
    }
}
