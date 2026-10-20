using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    // Private client-owned work. Applying it creates a separate unpublished Project.
    public class AIJobDraft
    {
        public bool IsDeleted { get; set; }
        [StringLength(80)] public string Industry { get; set; }
        public int Id { get; set; }
        public int ClientId { get; set; }
        public virtual UserAccount Client { get; set; }
        [Required, StringLength(1500)] public string Idea { get; set; }
        [Required, StringLength(16)] public string ProjectType { get; set; }
        public decimal Budget { get; set; }
        public DateTime? DeadlineUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public int? AppliedProjectId { get; set; }
        public virtual Project AppliedProject { get; set; }
        public virtual ICollection<AIJobDraftVersion> Versions { get; set; } = new List<AIJobDraftVersion>();
    }

    public class AIJobDraftVersion
    {
        public bool ExternalAiRequested { get; set; }
        [StringLength(40)] public string PromptVersion { get; set; }
        [StringLength(1000)] public string KnowledgeReferences { get; set; }
        public int Id { get; set; }
        public int AIJobDraftId { get; set; }
        public virtual AIJobDraft Draft { get; set; }
        public int VersionNumber { get; set; }
        [Required, StringLength(180)] public string Title { get; set; }
        [Required, StringLength(4000)] public string Description { get; set; }
        [Required, StringLength(2000)] public string Objectives { get; set; }
        [Required, StringLength(2000)] public string Scope { get; set; }
        [Required, StringLength(2000)] public string UserStories { get; set; }
        [Required, StringLength(1000)] public string RequiredSkills { get; set; }
        [Required, StringLength(2000)] public string Deliverables { get; set; }
        [Required, StringLength(2000)] public string Milestones { get; set; }
        [Required, StringLength(40)] public string Generator { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public virtual ICollection<AIJobRoleSuggestion> Roles { get; set; } = new List<AIJobRoleSuggestion>();
    }

    public class AIJobRoleSuggestion
    {
        public int Id { get; set; }
        public int AIJobDraftVersionId { get; set; }
        public virtual AIJobDraftVersion Version { get; set; }
        [Required, StringLength(100)] public string Title { get; set; }
        [Range(1, 50)] public int RequiredCount { get; set; }
    }
}
