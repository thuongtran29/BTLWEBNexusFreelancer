using System.Collections.Generic;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;
namespace NexusFreelance.Web.ViewModels
{
    public sealed class ContractDetailsPage
    {
        public ProjectContract Contract { get; set; }
        public ContractVersion Version { get; set; }
        public ContractSnapshot Snapshot { get; set; }
        public List<ContractAcceptance> Acceptances { get; set; }
        public List<EscrowMilestone> Milestones { get; set; }
        public List<PaymentIntent> Payments { get; set; }
        public List<PspRefund> Refunds { get; set; }
        public List<EscrowDispute> Disputes { get; set; }
        public List<ContractVersion> Versions { get; set; }
        public int WorkspaceId { get; set; }
        public bool IsOwner { get; set; }
        public bool IsAdmin { get; set; }
        public bool HasConfirmed { get; set; }
        public bool CanRevise { get; set; }
    }
}
