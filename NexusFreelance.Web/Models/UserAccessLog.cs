using System;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    // Sampled successful page views only. Never store URLs, query strings, IPs or payloads.
    public class UserAccessLog
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        [Required, StringLength(60)] public string Controller { get; set; }
        [Required, StringLength(60)] public string Action { get; set; }
        public DateTime AccessedAtUtc { get; set; }
    }
}
