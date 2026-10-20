using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.ViewModels
{
    public class GroupBidCandidateForm
    {
        public bool Selected { get; set; }
        public int UserAccountId { get; set; }
        public int RoleRequirementId { get; set; }
        [Range(0, 1000000000000)] public decimal QuotedPrice { get; set; }
    }

    public class GroupBidForm
    {
        [Range(1,3650)] public int DeliveryDays { get; set; } = 14;
        [StringLength(4000)] public string MilestonePlan { get; set; }
        public int FreelancerGroupId { get; set; }
        public int ProjectId { get; set; }
        [Required, StringLength(2500)] public string Pitch { get; set; }
        public List<GroupBidCandidateForm> Candidates { get; set; } = new List<GroupBidCandidateForm>();
    }

    public class GroupBidPage
    {
        public FreelancerGroup Group { get; set; }
        public Project Project { get; set; }
        public List<FreelancerGroupMember> Members { get; set; }
        public GroupBidForm Form { get; set; }
    }
}
