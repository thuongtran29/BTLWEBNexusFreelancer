namespace NexusFreelance.Web.Services
{
    public static class ContractPresentation
    {
        public static string LedgerKind(string value)
        {
            switch(value)
            {
                case "PspCapture":return "Tiền đã nhận qua VNPay";
                case "PspRefundReserve":return "Giữ tiền chờ hoàn qua VNPay";
                case "PspRefundRestore":return "Trả lại số dư do hoàn VNPay bị từ chối";
                case "Deposit":return "Nạp tiền đã đối soát";
                case "Withdrawal":return "Rút tiền đã đối soát";
                case "EscrowHold":return "Giữ tiền cho mốc hợp đồng";
                case "EscrowSettle":return "Quyết toán tiền đang giữ";
                case "Refund":return "Hoàn tiền mốc về ví";
                case "Payout":return "Thu nhập đã nghiệm thu";
                case "PlatformFee":return "Phí nền tảng";
                default:return value;
            }
        }
        public static string Status(string value)
        {
            switch(value)
            {
                case "Draft":return "Bản nháp";
                case "Published":return "Đang tuyển dụng";
                case "Paused":return "Tạm dừng tuyển";
                case "Cancelled":return "Đã hủy";
                case "InProgress":return "Đang thực hiện";
                case "AwaitingSignatures":return "Chờ các bên xác nhận";
                case "AwaitingEscrow":return "Chờ bảo đảm thanh toán";
                case "Active":return "Đang thực hiện";
                case "Completed":return "Đã hoàn thành";
                case "Terminated":return "Đã quyết toán và kết thúc";
                case "Declined":return "Đã từ chối ký kết";
                case "Disputed":return "Đang xử lý tranh chấp";
                case "Planned":return "Chưa bảo đảm";
                case "Funded":return "Đã bảo đảm";
                case "Submitted":return "Chờ nghiệm thu";
                case "Approved":return "Đã nghiệm thu";
                case "RevisionsRequested":return "Cần chỉnh sửa";
                case "Released":return "Đã giải ngân";
                case "Refunded":return "Đã quyết toán hoàn tiền";
                case "Pending":return "Chờ xác nhận";
                case "Paid":return "Đã nhận và giữ tiền cho mốc";
                case "PaidUnallocated":return "Đã nhận tiền, chờ đối soát phân bổ";
                case "Failed":return "Không thành công";
                case "Queued":return "Chờ gửi yêu cầu";
                case "Processing":return "Đang gửi yêu cầu";
                case "Unknown":return "Chưa rõ kết quả, cần đối soát";
                case "Succeeded":return "Đã hoàn thành qua VNPay";
                case "Resolved":return "Đã có quyết định";
                default:return value;
            }
        }
    }
}
