using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.ViewModels
{
    public class FreelancerProfileForm
    {
        public int? CoreSkillTrackId { get; set; }
        public System.Collections.Generic.List<int> SecondarySkillTrackIds { get; set; } = new System.Collections.Generic.List<int>();
        [Required, StringLength(100)] public string CoreRole { get; set; }
        [StringLength(1000)] public string Bio { get; set; }
        [StringLength(80)] public string Industry { get; set; }
        [StringLength(120)] public string Location { get; set; }
        [Range(0,60)] public int YearsExperience { get; set; }
        [Range(typeof(decimal), "0", "999999999999")] public decimal MinimumProjectBudget { get; set; }
        public bool IsAvailable { get; set; }
        public bool IsPublic { get; set; } = true;
    }

    public class PortfolioItemForm
    {
        [StringLength(100)] public string Role { get; set; }
        [StringLength(300)] public string Skills { get; set; }
        [StringLength(1000)] public string Outcome { get; set; }
        public int? ProjectId { get; set; }
        [Required, StringLength(150)] public string Title { get; set; }
        [Required, StringLength(1500)] public string Summary { get; set; }
        [Required, Models.PortfolioUrl, StringLength(1000)] public string Url { get; set; }
    }

    public class FreelancerProfilePage
    {
        public bool HasInactiveRoleSelections { get; set; }
        public System.Collections.Generic.List<VerifiedSkillRow> VerifiedSkills { get; set; } = new System.Collections.Generic.List<VerifiedSkillRow>();
        public FreelancerProfileForm Profile { get; set; }
        public System.Collections.Generic.List<Models.PortfolioItem> Portfolio { get; set; }
        public System.Collections.Generic.List<ProjectChoice> CompletedProjects { get; set; }
    }
    public class ProjectChoice
    {
        public int Id { get; set; }
        public string Title { get; set; }
    }
}
