using System;
using System.Collections.Generic;

namespace NexusFreelance.Web.ViewModels
{
    // Only explicitly selected public fields belong in these view models.
    public class PublicFreelancerCard
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string CoreRole { get; set; }
        public string Bio { get; set; }
        public bool IsAvailable { get; set; }
    }

    public class PublicGroupCard
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string LeaderName { get; set; }
        public int MemberCount { get; set; }
    }

    public class PublicTalentDirectoryPage
    {
        public string Query { get; set; }
        public string Kind { get; set; }
        public int Page { get; set; }
        public int Total { get; set; }
        public List<PublicFreelancerCard> Freelancers { get; set; } = new List<PublicFreelancerCard>();
        public List<PublicGroupCard> Groups { get; set; } = new List<PublicGroupCard>();
    }

    public class PublicFreelancerPage
    {
        public PublicFreelancerCard Freelancer { get; set; }
        public List<Models.PortfolioItem> Portfolio { get; set; } = new List<Models.PortfolioItem>();
        public List<Models.EarnedSkillBadge> Badges { get; set; } = new List<Models.EarnedSkillBadge>();
        public List<Models.ExternalCertification> Certifications { get; set; } = new List<Models.ExternalCertification>();
    }

    public class PublicGroupPage
    {
        public PublicGroupCard Group { get; set; }
        public int LeaderId { get; set; }
        public bool HasLeaderProfile { get; set; }
        public string Industry { get; set; }
        public string WorkStyle { get; set; }
        public string Specialties { get; set; }
        public string Collaboration { get; set; }
        public decimal? MinimumBudget { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public List<PublicFreelancerCard> Members { get; set; } = new List<PublicFreelancerCard>();
        public List<PublicGroupPortfolioItem> Portfolio { get; set; } = new List<PublicGroupPortfolioItem>();
    }

    public class PublicGroupPortfolioItem
    {
        public int FreelancerId { get; set; }
        public string FreelancerName { get; set; }
        public string Title { get; set; }
        public string Summary { get; set; }
        public string Role { get; set; }
        public string Url { get; set; }
    }
}
