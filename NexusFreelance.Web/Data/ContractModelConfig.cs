using System.Data.Entity;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Data
{
    internal static class ContractModelConfig
    {
        internal static void Configure(DbModelBuilder m)
        {
            m.Entity<ProjectContract>().ToTable("Contracts");
            m.Entity<ProjectContract>().HasRequired(x=>x.Project).WithMany().HasForeignKey(x=>x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<ProjectContract>().HasRequired(x=>x.Counterparty).WithMany().HasForeignKey(x=>x.CounterpartyId).WillCascadeOnDelete(false);
            m.Entity<ProjectContract>().HasOptional(x=>x.Proposal).WithMany().HasForeignKey(x=>x.ProposalId).WillCascadeOnDelete(false);
            m.Entity<ProjectContract>().HasOptional(x=>x.GroupBid).WithMany().HasForeignKey(x=>x.GroupBidId).WillCascadeOnDelete(false);
            m.Entity<ProjectContract>().HasIndex(x=>x.SourceKey).IsUnique();
            m.Entity<ContractVersion>().ToTable("ContractVersions");
            m.Entity<ContractVersion>().HasRequired(x=>x.Contract).WithMany().HasForeignKey(x=>x.ContractId).WillCascadeOnDelete(false);
            m.Entity<ContractVersion>().HasIndex(x=>new{x.ContractId,x.VersionNumber}).IsUnique();
            m.Entity<ContractVersion>().Property(x=>x.Price).HasPrecision(18,0);
            m.Entity<ContractVersion>().Property(x=>x.FeeRate).HasPrecision(6,4);
            m.Entity<ContractMember>().ToTable("ContractMembers");
            m.Entity<ContractMember>().HasRequired(x=>x.Contract).WithMany().HasForeignKey(x=>x.ContractId).WillCascadeOnDelete(false);
            m.Entity<ContractMember>().HasRequired(x=>x.User).WithMany().HasForeignKey(x=>x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<ContractMember>().HasRequired(x=>x.Proposal).WithMany().HasForeignKey(x=>x.ProposalId).WillCascadeOnDelete(false);
            m.Entity<ContractMember>().HasIndex(x=>new{x.ContractId,x.UserAccountId}).IsUnique();
            m.Entity<ContractMember>().HasIndex(x=>x.ProposalId).IsUnique();
            m.Entity<ContractAcceptance>().ToTable("ContractAcceptances");
            m.Entity<ContractAcceptance>().HasRequired(x=>x.Version).WithMany().HasForeignKey(x=>x.ContractVersionId).WillCascadeOnDelete(false);
            m.Entity<ContractAcceptance>().HasRequired(x=>x.User).WithMany().HasForeignKey(x=>x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<ContractAcceptance>().HasIndex(x=>new{x.ContractVersionId,x.UserAccountId}).IsUnique();
            m.Entity<EscrowMilestone>().HasOptional(x=>x.Contract).WithMany().HasForeignKey(x=>x.ContractId).WillCascadeOnDelete(false);
            m.Entity<EscrowShare>().HasOptional(x=>x.Contract).WithMany().HasForeignKey(x=>x.ContractId).WillCascadeOnDelete(false);
            m.Entity<EscrowDispute>().HasOptional(x=>x.Contract).WithMany().HasForeignKey(x=>x.ContractId).WillCascadeOnDelete(false);
            m.Entity<Workspace>().HasOptional(x=>x.Contract).WithMany().HasForeignKey(x=>x.ContractId).WillCascadeOnDelete(false);
        }
    }
}
