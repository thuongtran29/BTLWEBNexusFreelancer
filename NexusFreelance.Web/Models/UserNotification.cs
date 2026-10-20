using System;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    public class UserNotification
    {
        public int Id { get; set; }
        public int RecipientId { get; set; }
        public virtual UserAccount Recipient { get; set; }
        public int? ActorId { get; set; }
        public virtual UserAccount Actor { get; set; }
        public int? ProjectId { get; set; }
        public virtual Project Project { get; set; }
        [StringLength(80)] public string Source { get; set; }
        [Required, StringLength(200)] public string Title { get; set; }
        [Required, StringLength(250)] public string TargetPath { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ReadAtUtc { get; set; }
    }
}
