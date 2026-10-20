using System;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    public class ProjectContract
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int? ProposalId { get; set; }
        public virtual Proposal Proposal { get; set; }
        public int? GroupBidId { get; set; }
        public virtual GroupBid GroupBid { get; set; }
        public int CounterpartyId { get; set; }
        public virtual UserAccount Counterparty { get; set; }
        [Required, StringLength(64)] public string SourceKey { get; set; }
        [Required, StringLength(24)] public string Status { get; set; }
        public int CurrentVersion { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
    public class ContractVersion
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public virtual ProjectContract Contract { get; set; }
        public int VersionNumber { get; set; }
        [Required] public string SnapshotJson { get; set; }
        [Required, StringLength(64)] public string Hash { get; set; }
        public decimal Price { get; set; }
        public decimal FeeRate { get; set; }
        public int DeliveryDays { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
    public class ContractMember
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public virtual ProjectContract Contract { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public int ProposalId { get; set; }
        public virtual Proposal Proposal { get; set; }
        public decimal Weight { get; set; }
    }
    public class ContractAcceptance
    {
        public int Id { get; set; }
        public int ContractVersionId { get; set; }
        public virtual ContractVersion Version { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        [Required, StringLength(64)] public string AcceptedHash { get; set; }
        public DateTime AcceptedAtUtc { get; set; }
    }
}
