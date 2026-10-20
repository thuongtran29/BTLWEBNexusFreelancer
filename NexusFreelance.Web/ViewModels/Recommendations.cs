using System.Collections.Generic;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.ViewModels
{
    public class VerifiedSkillRow
    {
        public int SkillId { get; set; }
        public string Title { get; set; }
        public int Level { get; set; }
        public string Evidence { get; set; }
    }
    public class MatchCard
    {
        public int Id { get; set; }
        public string Kind { get; set; }
        public string Name { get; set; }
        public string Subtitle { get; set; }
        public int Score { get; set; }
        public int Covered { get; set; }
        public int Required { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
    }
    public class SkillMatchPage
    {
        public Project Project { get; set; }
        public List<ProjectSkillRequirement> Skills { get; set; } = new List<ProjectSkillRequirement>();
        public List<PositionRecommendation> Positions { get; set; } = new List<PositionRecommendation>();
        public List<MatchCard> People { get; set; } = new List<MatchCard>();
        public List<MatchCard> Groups { get; set; } = new List<MatchCard>();
    }
}
