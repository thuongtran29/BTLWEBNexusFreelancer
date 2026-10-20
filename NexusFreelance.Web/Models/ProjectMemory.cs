using System;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    // Every edit creates an immutable version. Nothing here is automatically published.
    public class ProjectMemory
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int EditorId { get; set; }
        public virtual UserAccount Editor { get; set; }
        public int Version { get; set; }
        [Required, StringLength(30)] public string Source { get; set; }
        [Required, StringLength(2000)] public string ExecutiveSummary { get; set; }
        [StringLength(600)] public string TechStack { get; set; }
        [StringLength(2000)] public string ChallengesAndSolutions { get; set; }
        [StringLength(2000)] public string LessonsLearned { get; set; }
        [StringLength(1000)] public string ReusableAssets { get; set; }
        [StringLength(1500)] public string PortfolioDraft { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
