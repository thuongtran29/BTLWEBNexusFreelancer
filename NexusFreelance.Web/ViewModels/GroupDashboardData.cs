using System.Collections.Generic;

namespace NexusFreelance.Web.ViewModels
{
    // Project data on a group page is visible only to members of that project's team.
    public class GroupDashboardData
    {
        public List<GroupProjectProgress> Projects { get; set; } = new List<GroupProjectProgress>();
        public Dictionary<int, string> MemberRoles { get; set; } = new Dictionary<int, string>();
    }

    public class GroupProjectProgress
    {
        public int? ContractId { get; set; }
        public int ProjectId { get; set; }
        public int WorkspaceId { get; set; }
        public string ProjectTitle { get; set; }
        public string ProjectStatus { get; set; }
        public int TaskCount { get; set; }
        public int DoneTaskCount { get; set; }
        public int ContributorCount { get; set; }
        public int EvidenceScoreTotal { get; set; }
        public bool HasProjectMemory { get; set; }
    }
}
