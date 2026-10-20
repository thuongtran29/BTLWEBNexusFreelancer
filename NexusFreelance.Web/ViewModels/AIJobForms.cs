using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.ViewModels
{
    public class JobIdeaForm
    {
        [StringLength(80)] public string Industry { get; set; }
        [Required, StringLength(1500, MinimumLength = 10)]
        public string Idea { get; set; }
        [Required] public string ProjectType { get; set; } = "TEAM";
        [Range(typeof(decimal), "1", "999999999999")] public decimal Budget { get; set; }
        [DataType(DataType.Date)] public DateTime? Deadline { get; set; }
        // Only an explicit choice on this request permits third-party generation.
        public bool UseExternalAi { get; set; }
        [StringLength(10)] public string ExternalAiProvider { get; set; }
        public bool UseProjectKnowledge { get; set; } = true;
        [StringLength(600)] public string AdditionalInstructions { get; set; }
        // Server populated after authorization, excluded from HTTP model binding.
        public string KnowledgeContext { get; set; }
        public string KnowledgeReferences { get; set; }
        public string SharedPracticeHints { get; set; }
        public string SharedKnowledgeReferences { get; set; }
        public string GenerationAngle { get; set; }
        public bool UseSharedKnowledge { get; set; } = true;
    }

    public class JobRoleForm
    {
        [StringLength(100)] public string Title { get; set; }
        [Range(1, 50)] public int RequiredCount { get; set; } = 1;
    }

    public class JobEditForm
    {
        public int Id { get; set; }
        public int ExpectedVersionId { get; set; }
        [Required, StringLength(180)] public string Title { get; set; }
        [Required, StringLength(4000)] public string Description { get; set; }
        [Required, StringLength(2000)] public string Objectives { get; set; }
        [Required, StringLength(2000)] public string Scope { get; set; }
        [Required, StringLength(2000)] public string UserStories { get; set; }
        [Required, StringLength(1000)] public string RequiredSkills { get; set; }
        [Required, StringLength(2000)] public string Deliverables { get; set; }
        [Required, StringLength(2000)] public string Milestones { get; set; }
        public List<JobRoleForm> Roles { get; set; } = new List<JobRoleForm>();
    }
}
