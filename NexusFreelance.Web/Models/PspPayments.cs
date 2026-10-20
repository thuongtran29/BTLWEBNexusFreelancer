using System;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    public class PaymentIntent
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public virtual ProjectContract Contract { get; set; }
        public int EscrowMilestoneId { get; set; }
        public virtual EscrowMilestone Milestone { get; set; }
        [Required,StringLength(32)] public string TxnRef { get; set; }
        [Required,StringLength(16)] public string Environment { get; set; }
        [Required,StringLength(16)] public string MerchantCode { get; set; }
        [Required,StringLength(24)] public string Status { get; set; }
        [Required,StringLength(3)] public string Currency { get; set; } = "VND";
        public decimal Amount { get; set; }
        [StringLength(64)] public string ProviderTransactionNo { get; set; }
        [Required,StringLength(14)] public string TransactionDate { get; set; }
        [Required,StringLength(2048)] public string CheckoutUrl { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? PaidAtUtc { get; set; }
        [StringLength(2)] public string ResponseCode { get; set; }
    }
    public class PspReceipt
    {
        public int Id { get; set; }
        public int PaymentIntentId { get; set; }
        public virtual PaymentIntent Payment { get; set; }
        [Required,StringLength(64)] public string Digest { get; set; }
        [Required,StringLength(24)] public string Source { get; set; }
        [Required,StringLength(2)] public string ResponseCode { get; set; }
        public DateTime ReceivedAtUtc { get; set; }
    }
    public class PspRefund
    {
        public int Id { get; set; }
        public int PaymentIntentId { get; set; }
        public virtual PaymentIntent Payment { get; set; }
        public int RequestedById { get; set; }
        public virtual UserAccount RequestedBy { get; set; }
        [Required,StringLength(32)] public string RequestId { get; set; }
        [Required,StringLength(24)] public string Status { get; set; }
        [Required,StringLength(300)] public string Reason { get; set; }
        public decimal Amount { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? AttemptedAtUtc { get; set; }
        public DateTime? ReconciledAtUtc { get; set; }
        [StringLength(64)] public string ResponseDigest { get; set; }
        [StringLength(64)] public string ProviderTransactionNo { get; set; }
        [StringLength(2)] public string ResponseCode { get; set; }
    }
}
