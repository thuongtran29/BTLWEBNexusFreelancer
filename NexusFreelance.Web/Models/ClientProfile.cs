using System;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    public class ClientProfile
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        [Required, StringLength(16)] public string ClientType { get; set; }
        [StringLength(180)] public string CompanyName { get; set; }
        [StringLength(120)] public string Location { get; set; }
        [StringLength(1000)] public string About { get; set; }
        [StringLength(1000)] public string Website { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
    public class ClientReview
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int FreelancerId { get; set; }
        public virtual UserAccount Freelancer { get; set; }
        public int ClientId { get; set; }
        public virtual UserAccount Client { get; set; }
        [Range(1,5)] public int Rating { get; set; }
        [Required, StringLength(1000)] public string Comment { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
