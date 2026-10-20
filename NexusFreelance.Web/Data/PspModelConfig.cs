using System.Data.Entity;
using NexusFreelance.Web.Models;
namespace NexusFreelance.Web.Data
{
    internal static class PspModelConfig
    {
        internal static void Configure(DbModelBuilder m)
        {
            m.Entity<PaymentIntent>().ToTable("PaymentIntents");
            m.Entity<PaymentIntent>().HasRequired(x=>x.Contract).WithMany().HasForeignKey(x=>x.ContractId).WillCascadeOnDelete(false);
            m.Entity<PaymentIntent>().HasRequired(x=>x.Milestone).WithMany().HasForeignKey(x=>x.EscrowMilestoneId).WillCascadeOnDelete(false);
            m.Entity<PaymentIntent>().HasIndex(x=>x.TxnRef).IsUnique();
            m.Entity<PaymentIntent>().Property(x=>x.Amount).HasPrecision(18,0);
            m.Entity<PspReceipt>().ToTable("PspReceipts");
            m.Entity<PspReceipt>().HasRequired(x=>x.Payment).WithMany().HasForeignKey(x=>x.PaymentIntentId).WillCascadeOnDelete(false);
            m.Entity<PspReceipt>().HasIndex(x=>x.Digest).IsUnique();
            m.Entity<PspRefund>().ToTable("PspRefunds");
            m.Entity<PspRefund>().HasRequired(x=>x.Payment).WithMany().HasForeignKey(x=>x.PaymentIntentId).WillCascadeOnDelete(false);
            m.Entity<PspRefund>().HasRequired(x=>x.RequestedBy).WithMany().HasForeignKey(x=>x.RequestedById).WillCascadeOnDelete(false);
            m.Entity<PspRefund>().HasIndex(x=>x.RequestId).IsUnique();
            // One refund operation per provider payment avoids ambiguous timeout retries.
            m.Entity<PspRefund>().HasIndex(x=>x.PaymentIntentId).IsUnique();
            m.Entity<PspRefund>().Property(x=>x.Amount).HasPrecision(18,0);
        }
    }
}
