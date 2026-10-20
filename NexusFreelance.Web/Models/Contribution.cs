using System;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    // An immutable snapshot when a Client submits the project's final member review.
    // Counts are evidence signals, not a measurement of individual productivity.
    public class ContributionRecord
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public int ReviewId { get; set; }
        public virtual Review Review { get; set; }
        public int DoneTasks { get; set; }
        public int ApprovedDeliverables { get; set; }
        public int ClientRating { get; set; }
        public int EvidenceScore { get; set; }
        [Required, StringLength(40)] public string Method { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    // Append-only positive XP awards, linked to a single verified contribution snapshot.
    public class XPTransaction
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public int ContributionRecordId { get; set; }
        public virtual ContributionRecord Contribution { get; set; }
        public int Points { get; set; }
        [Required, StringLength(120)] public string Reason { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    // Historical role evidence from a completed project; no skill proficiency is certified.
    public class SkillDNAHistory
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public int ContributionRecordId { get; set; }
        public virtual ContributionRecord Contribution { get; set; }
        [Required, StringLength(100)] public string RoleTitle { get; set; }
        public int EvidenceScore { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
