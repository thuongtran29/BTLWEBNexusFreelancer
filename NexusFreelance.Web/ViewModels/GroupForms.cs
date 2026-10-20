using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.ViewModels
{
    public class CreateGroupForm
    {
        [Required, StringLength(120)] public string Name { get; set; }
        [Required, StringLength(1000)] public string Description { get; set; }
        public bool IsPublic { get; set; } = true;
        [StringLength(80)] public string Industry { get; set; }
        [StringLength(24), RegularExpression("^(Remote|Hybrid|Onsite)$")] public string WorkStyle { get; set; } = "Remote";
        [StringLength(300)] public string Specialties { get; set; }
        [StringLength(300)] public string Collaboration { get; set; }
        [Range(1,50)] public int TargetSize { get; set; } = 5;
        [Range(typeof(decimal),"0","999999999999")] public decimal MinimumBudget { get; set; }
    }

    public class GroupInviteForm
    {
        public int GroupId { get; set; }
        [Required, EmailAddress, StringLength(200)] public string Email { get; set; }
    }

    public class GroupIndexPage
    {
        public List<FreelancerGroupMember> Memberships { get; set; }
        public List<FreelancerGroupMember> Invitations { get; set; }
    }

    public class GroupDetailsPage
    {
        public FreelancerGroup Group { get; set; }
        public List<FreelancerGroupMember> Members { get; set; }
        public List<FreelancerGroupEvent> Events { get; set; }
        public bool IsLeader { get; set; }
        public bool IsLeaderVisibleInDirectory { get; set; }
    }
}
