using System;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    // Certificates are self-reported until an administrator has reviewed their evidence.
    public class ExternalCertification
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public int? SkillTrackId { get; set; }
        public virtual SkillTrack Skill { get; set; }
        public int? ProficiencyLevel { get; set; }
        [Required, StringLength(180)] public string Name { get; set; }
        [Required, StringLength(160)] public string Issuer { get; set; }
        [Required, StringLength(1000)] public string EvidenceUrl { get; set; }
        public DateTime IssuedOn { get; set; }
        public DateTime? ExpiresOn { get; set; }
        [Required, StringLength(16)] public string Status { get; set; }
        [StringLength(500)] public string ReviewReason { get; set; }
        public int? ReviewedById { get; set; }
        public virtual UserAccount ReviewedBy { get; set; }
        public DateTime SubmittedAtUtc { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
    }
}
