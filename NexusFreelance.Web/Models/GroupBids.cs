using System;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    // A group bid is an atomic offer containing one individual proposal per candidate and role.
    public class GroupBid
    {
        [Range(1,3650)] public int DeliveryDays { get; set; } = 14;
        [StringLength(4000)] public string MilestonePlan { get; set; }
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int FreelancerGroupId { get; set; }
        public virtual FreelancerGroup Group { get; set; }
        public int LeaderId { get; set; }
        public virtual UserAccount Leader { get; set; }
        [Required, StringLength(2500)] public string Pitch { get; set; }
        [Required, StringLength(16)] public string Status { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
