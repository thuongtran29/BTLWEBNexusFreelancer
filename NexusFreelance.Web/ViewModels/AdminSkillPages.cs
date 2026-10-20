using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.ViewModels
{
    public class AdminSkillCatalogPage
    {
        public IList<SkillTrack> Tracks { get; set; } = new List<SkillTrack>();
    }

    public class AdminQuestionBankPage
    {
        public SkillTrack Track { get; set; }
        public IList<SkillExamQuestion> Questions { get; set; } = new List<SkillExamQuestion>();
        public ISet<int> LockedQuestionIds { get; set; } = new HashSet<int>();
    }

    public class AdminCreateSkillForm
    {
        [StringLength(80)] public string Industry { get; set; }
        public bool IsActive { get; set; } = true;
        [Required, StringLength(40, MinimumLength = 3)]
        [RegularExpression(@"^[A-Za-z][A-Za-z0-9_]*$")]
        public string Code { get; set; }

        [Required, StringLength(120)] public string Title { get; set; }
        [Required, StringLength(600)] public string Description { get; set; }
        [Required, StringLength(250)] public string MatchKeywords { get; set; }
    }

    public class AdminUpdateSkillForm
    {
        [StringLength(80)] public string Industry { get; set; }
        public bool IsActive { get; set; } = true;
        [Range(1, int.MaxValue)] public int Id { get; set; }
        [Required, StringLength(120)] public string Title { get; set; }
        [Required, StringLength(600)] public string Description { get; set; }
        [Required, StringLength(250)] public string MatchKeywords { get; set; }
    }

    public class AdminSaveQuestionForm
    {
        [Range(0, int.MaxValue)] public int Id { get; set; }
        [Range(1, int.MaxValue)] public int SkillTrackId { get; set; }
        [Range(1, 1000)] public int Ordinal { get; set; }
        [Required, StringLength(500)] public string Prompt { get; set; }
        [Required, StringLength(300)] public string ChoiceA { get; set; }
        [Required, StringLength(300)] public string ChoiceB { get; set; }
        [Required, StringLength(300)] public string ChoiceC { get; set; }
        [Required, StringLength(300)] public string ChoiceD { get; set; }
        [Required, RegularExpression("^[ABCDabcd]$")] public string CorrectChoice { get; set; }
    }
}
