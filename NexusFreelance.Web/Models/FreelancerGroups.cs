using System;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    public class FreelancerGroup
    {
        public int Id { get; set; }
        public int LeaderId { get; set; }
        public virtual UserAccount Leader { get; set; }
        [Required, StringLength(120)] public string Name { get; set; }
        [Required, StringLength(1000)] public string Description { get; set; }
        // NULL is the legacy-public state; the leader can opt the group out.
        public bool? IsPublic { get; set; }
        [StringLength(80)] public string Industry { get; set; }
        [StringLength(24)] public string WorkStyle { get; set; }
        [StringLength(300)] public string Specialties { get; set; }
        [StringLength(300)] public string Collaboration { get; set; }
        public int? TargetSize { get; set; }
        public decimal? MinimumBudget { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    // One historical row per (group,user); a new invitation reopens a declined/left membership.
    public class FreelancerGroupMember
    {
        public int Id { get; set; }
        public int FreelancerGroupId { get; set; }
        public virtual FreelancerGroup Group { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        [Required, StringLength(12)] public string Status { get; set; }
        public DateTime InvitedAtUtc { get; set; }
        public DateTime? RespondedAtUtc { get; set; }
    }

    public class FreelancerGroupEvent
    {
        public int Id { get; set; }
        public int FreelancerGroupId { get; set; }
        public virtual FreelancerGroup Group { get; set; }
        public int ActorId { get; set; }
        public virtual UserAccount Actor { get; set; }
        [Required, StringLength(180)] public string Description { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
