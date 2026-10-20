using System.Collections.Generic;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.ViewModels
{
    public class SkillCenterPage
    {
        public List<SkillTrack> Tracks { get; set; }
        public List<EarnedSkillBadge> Badges { get; set; }
        public List<EarnedSkillBadge> RevokedBadges { get; set; }
        public List<SkillExamAttempt> RecentAttempts { get; set; }
    }

    // Only these fields (never CorrectChoice) are sent to the exam page.
    public class PublicExamQuestion
    {
        public int Id { get; set; }
        public int Ordinal { get; set; }
        public string Prompt { get; set; }
        public string ChoiceA { get; set; }
        public string ChoiceB { get; set; }
        public string ChoiceC { get; set; }
        public string ChoiceD { get; set; }
    }

    public class SkillExamPage
    {
        public int SkillTrackId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int RemainingAttempts { get; set; }
        public bool EarnedBadge { get; set; }
        public List<PublicExamQuestion> Questions { get; set; }
        public string ExamTicket { get; set; }
        public int ExamDurationSeconds { get; set; }
    }

    public class ExamAnswerForm
    {
        public int QuestionId { get; set; }
        public string Choice { get; set; }
    }

    public class ExamSubmissionForm
    {
        public int SkillTrackId { get; set; }
        public List<ExamAnswerForm> Answers { get; set; }
        public string ExamTicket { get; set; }
        public int BrowserFocusLossCount { get; set; }
    }
}
