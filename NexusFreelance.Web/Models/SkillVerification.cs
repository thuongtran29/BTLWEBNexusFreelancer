using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    // Internally assessed introductory skills. Badges are not external certifications.
    public class SkillTrack
    {
        [StringLength(80)] public string Industry { get; set; }
        public bool? IsActive { get; set; }
        public int Id { get; set; }
        [Required, StringLength(40)] public string Code { get; set; }
        [Required, StringLength(120)] public string Title { get; set; }
        [Required, StringLength(600)] public string Description { get; set; }
        [Required, StringLength(250)] public string MatchKeywords { get; set; }
        public virtual ICollection<SkillExamQuestion> Questions { get; set; } = new List<SkillExamQuestion>();
    }

    public class SkillExamQuestion
    {
        public int Id { get; set; }
        public int SkillTrackId { get; set; }
        public virtual SkillTrack SkillTrack { get; set; }
        public int Ordinal { get; set; }
        [Required, StringLength(500)] public string Prompt { get; set; }
        [Required, StringLength(300)] public string ChoiceA { get; set; }
        [Required, StringLength(300)] public string ChoiceB { get; set; }
        [Required, StringLength(300)] public string ChoiceC { get; set; }
        [Required, StringLength(300)] public string ChoiceD { get; set; }
        [Required, StringLength(1)] public string CorrectChoice { get; set; }
    }

    public class SkillExamAttempt
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public int SkillTrackId { get; set; }
        public virtual SkillTrack SkillTrack { get; set; }
        public int QuestionCount { get; set; }
        public int CorrectCount { get; set; }
        public int Percent { get; set; }
        public bool Passed { get; set; }
        public DateTime SubmittedAtUtc { get; set; }
        // Nullable to preserve historical attempts from before timed exams.
        public DateTime? StartedAtUtc { get; set; }
        public int? DurationSeconds { get; set; }
        // Browser-reported visibility events are informational, not proof of misconduct.
        public int? BrowserFocusLossCount { get; set; }
        public virtual ICollection<SkillExamAnswer> Answers { get; set; } = new List<SkillExamAnswer>();
    }

    public class SkillExamAnswer
    {
        public int Id { get; set; }
        public int SkillExamAttemptId { get; set; }
        public virtual SkillExamAttempt Attempt { get; set; }
        public int SkillExamQuestionId { get; set; }
        public virtual SkillExamQuestion Question { get; set; }
        [Required, StringLength(1)] public string SelectedChoice { get; set; }
        public bool IsCorrect { get; set; }
    }

    // One live exam sitting per Freelancer/track. Refreshing a page keeps its deadline;
    // a completed sitting permits another only if the 24-hour attempt quota allows it.
    public class SkillExamSession
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public int SkillTrackId { get; set; }
        public virtual SkillTrack SkillTrack { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    public class EarnedSkillBadge
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public int SkillTrackId { get; set; }
        public virtual SkillTrack SkillTrack { get; set; }
        public int EvidenceAttemptId { get; set; }
        public virtual SkillExamAttempt EvidenceAttempt { get; set; }
        public DateTime IssuedAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
        public int? RevokedById { get; set; }
        public virtual UserAccount RevokedBy { get; set; }
        [StringLength(200)] public string RevocationReason { get; set; }
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string Tier => EvidenceAttempt == null ? "Bronze" :
            EvidenceAttempt.Percent >= 95 ? "Gold" : EvidenceAttempt.Percent >= 90 ? "Silver" : "Bronze";
    }
}
