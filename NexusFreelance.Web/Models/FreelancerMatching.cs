using System;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    public class FreelancerProfile
    {
        // Selected primary role; live evidence must still be valid before matching.
        public int? CoreSkillTrackId { get; set; }
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        // Legacy field retained as the self-reported professional title.
        [Required, StringLength(100)] public string CoreRole { get; set; }
        [StringLength(1000)] public string Bio { get; set; }
        [StringLength(80)] public string Industry { get; set; }
        [StringLength(120)] public string Location { get; set; }
        public int YearsExperience { get; set; }
        public decimal MinimumProjectBudget { get; set; }
        public bool IsAvailable { get; set; }
        // NULL preserves visibility for pre-existing development profiles.
        // Explicit false opts out of the anonymous Talent directory.
        public bool? IsPublic { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    // A role selection is not evidence. Enforce unique (UserAccountId, SkillTrackId)
    // in the context mapping and recheck current evidence when saving and matching.
    public class FreelancerSecondaryRole
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public int SkillTrackId { get; set; }
        public virtual SkillTrack SkillTrack { get; set; }
    }

    // Portfolio data is self-reported; the MVP does not verify its contents.
    public class PortfolioItem
    {
        [StringLength(100)] public string Role { get; set; }
        [StringLength(300)] public string Skills { get; set; }
        [StringLength(1000)] public string Outcome { get; set; }
        public int? ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int Id { get; set; }
        public int FreelancerId { get; set; }
        public virtual UserAccount Freelancer { get; set; }
        [Required, StringLength(150)] public string Title { get; set; }
        [Required, StringLength(1500)] public string Summary { get; set; }
        [Required, PortfolioUrl, StringLength(1000)] public string Url { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    // Immutable scoring snapshot for each submitted proposal.
    public class MatchingResult
    {
        public int Id { get; set; }
        public int ProposalId { get; set; }
        public virtual Proposal Proposal { get; set; }
        public int CoreRolePoints { get; set; }
        public int BadgePoints { get; set; }
        public int PortfolioPoints { get; set; }
        public int AvailabilityPoints { get; set; }
        public int ReliabilityPoints { get; set; }
        public int BudgetPoints { get; set; }
        public int TotalPoints { get; set; }
        [Required, StringLength(1500)] public string Explanation { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
