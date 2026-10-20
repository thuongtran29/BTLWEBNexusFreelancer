using System;
using System.ComponentModel.DataAnnotations;
namespace NexusFreelance.Web.Models
{
    public class NegotiationMessage
    {
        public int Id { get; set; }
        public int ProposalId { get; set; }
        public virtual Proposal Proposal { get; set; }
        public int SenderId { get; set; }
        public virtual UserAccount Sender { get; set; }
        [Required, StringLength(2000)] public string Body { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
