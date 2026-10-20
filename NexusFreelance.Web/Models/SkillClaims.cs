using System;
using System.ComponentModel.DataAnnotations;
namespace NexusFreelance.Web.Models
{
    public class SkillClaim
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public int SkillTrackId { get; set; }
        public virtual SkillTrack Skill { get; set; }
        public int Level { get; set; }
        public int YearsExperience { get; set; }
        public bool IsDeleted { get; set; }
    }
    public class SkillEvidenceRequest
    {
        public int Id { get; set; }
        public int SkillClaimId { get; set; }
        public virtual SkillClaim Claim { get; set; }
        [Required, StringLength(16)] public string Status { get; set; }
        public DateTime SubmittedAtUtc { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
        public int? ReviewerId { get; set; }
        public virtual UserAccount Reviewer { get; set; }
        [StringLength(300)] public string Reason { get; set; }
        public DateTime? ValidUntil { get; set; }
    }
    public class SkillEvidence
    {
        public int Id { get; set; }
        public int SkillEvidenceRequestId { get; set; }
        public virtual SkillEvidenceRequest Request { get; set; }
        [Required, StringLength(16)] public string Type { get; set; }
        [StringLength(1000)] public string Url { get; set; }
        [StringLength(200)] public string Issuer { get; set; }
        public DateTime? IssuedOn { get; set; }
        public DateTime? ExpiresOn { get; set; }
        public int? SkillExamAttemptId { get; set; }
        public virtual SkillExamAttempt Attempt { get; set; }
        public int? PortfolioItemId { get; set; }
        public virtual PortfolioItem Portfolio { get; set; }
        public int? Score { get; set; }
    }
}
