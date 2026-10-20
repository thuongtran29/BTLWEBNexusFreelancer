using System;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    public class WalletAccount
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public decimal Balance { get; set; }
        public decimal Held { get; set; }
        [Required, StringLength(3)] public string Currency { get; set; } = "VND";
    }
    public class WalletEntry
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public int? EscrowMilestoneId { get; set; }
        public virtual EscrowMilestone Milestone { get; set; }
        [Required, StringLength(24)] public string Kind { get; set; }
        [Required, StringLength(180)] public string Reference { get; set; }
        public decimal Amount { get; set; }
        public decimal BalanceDelta { get; set; }
        public decimal HeldDelta { get; set; }
        [Required, StringLength(3)] public string Currency { get; set; } = "VND";
        public DateTime CreatedAtUtc { get; set; }
    }
    public class MoneyRequest
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        [Required, StringLength(16)] public string Kind { get; set; }
        [Required, StringLength(16)] public string Status { get; set; }
        [Required, StringLength(100)] public string Reference { get; set; }
        [Required, StringLength(300)] public string Details { get; set; }
        [StringLength(300)] public string Resolution { get; set; }
        public decimal Amount { get; set; }
        public int? ReviewerId { get; set; }
        public virtual UserAccount Reviewer { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
    }
    public class EscrowMilestone
    {
        public int? DueDay { get; set; }
        [StringLength(1000)] public string AcceptanceCriteria { get; set; }
        public int? ContractId { get; set; }
        public virtual ProjectContract Contract { get; set; }
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        [Required, StringLength(180)] public string Title { get; set; }
        [Required, StringLength(20)] public string Status { get; set; }
        public decimal Amount { get; set; }
        public decimal Fee { get; set; }
        public decimal Refunded { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? SubmittedAtUtc { get; set; }
        public DateTime? ReviewRemindedAtUtc { get; set; }
        public DateTime? SettledAtUtc { get; set; }
    }
    public class EscrowShare
    {
        public int? ContractId { get; set; }
        public virtual ProjectContract Contract { get; set; }
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public decimal Weight { get; set; }
    }
    // System-generated event: deliberately has no user ActorId.
    public class MilestoneReviewReminder
    {
        public int Id { get; set; }
        public int EscrowMilestoneId { get; set; }
        public virtual EscrowMilestone Milestone { get; set; }
        public int DeliverableId { get; set; }
        public virtual Deliverable Delivery { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
    public class EscrowDispute
    {
        public int? ContractId { get; set; }
        public virtual ProjectContract Contract { get; set; }
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int ReporterId { get; set; }
        public virtual UserAccount Reporter { get; set; }
        [Required, StringLength(2000)] public string Reason { get; set; }
        [Required, StringLength(16)] public string Status { get; set; }
        [StringLength(1000)] public string Resolution { get; set; }
        public int? ReviewerId { get; set; }
        public virtual UserAccount Reviewer { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
    }
}
