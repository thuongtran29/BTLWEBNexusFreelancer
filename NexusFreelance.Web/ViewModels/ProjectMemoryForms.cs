using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.ViewModels
{
    public class ProjectMemoryForm
    {
        public int ProjectId { get; set; }
        public int ExpectedVersion { get; set; }
        [Required, StringLength(2000)] public string ExecutiveSummary { get; set; }
        [StringLength(600)] public string TechStack { get; set; }
        [StringLength(2000)] public string ChallengesAndSolutions { get; set; }
        [StringLength(2000)] public string LessonsLearned { get; set; }
        [StringLength(1000)] public string ReusableAssets { get; set; }
        [StringLength(1500)] public string PortfolioDraft { get; set; }
    }
}
