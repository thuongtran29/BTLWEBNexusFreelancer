using System;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    public class ProjectSkillRequirement
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int SkillTrackId { get; set; }
        public virtual SkillTrack Skill { get; set; }
        [Range(1, 5)] public int MinimumLevel { get; set; } = 1;
    }

    public class DirectConversation
    {
        public int Id { get; set; }
        public int ClientId { get; set; }
        public virtual UserAccount Client { get; set; }
        public int FreelancerId { get; set; }
        public virtual UserAccount Freelancer { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
    public class DirectMessage
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }
        public virtual DirectConversation Conversation { get; set; }
        public int SenderId { get; set; }
        public virtual UserAccount Sender { get; set; }
        [Required, StringLength(2000)] public string Body { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    // Separate order from escrow payments: a top-up never funds a contract implicitly.
    public class WalletTopup
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public decimal Amount { get; set; }
        [Required, StringLength(100)] public string TxnRef { get; set; }
        [Required, StringLength(16)] public string Status { get; set; }
        [Required, StringLength(16)] public string Environment { get; set; }
        [Required, StringLength(8)] public string MerchantCode { get; set; }
        [StringLength(40)] public string ProviderTransactionNo { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? CapturedAtUtc { get; set; }
    }
}
