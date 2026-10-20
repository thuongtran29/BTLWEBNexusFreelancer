using System.Data.Entity;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Data
{
    public static class PlatformModelConfig
    {
        public static void Configure(DbModelBuilder m)
        {
            m.Entity<ProjectSkillRequirement>().ToTable("ProjectSkillRequirements");
            m.Entity<ProjectSkillRequirement>().HasRequired(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<ProjectSkillRequirement>().HasRequired(x => x.Skill).WithMany().HasForeignKey(x => x.SkillTrackId).WillCascadeOnDelete(false);
            m.Entity<ProjectSkillRequirement>().HasIndex(x => new { x.ProjectId, x.SkillTrackId }).IsUnique();
            m.Entity<DirectConversation>().ToTable("DirectConversations");
            m.Entity<DirectConversation>().HasRequired(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).WillCascadeOnDelete(false);
            m.Entity<DirectConversation>().HasRequired(x => x.Freelancer).WithMany().HasForeignKey(x => x.FreelancerId).WillCascadeOnDelete(false);
            m.Entity<DirectConversation>().HasIndex(x => new { x.ClientId, x.FreelancerId }).IsUnique();
            m.Entity<DirectMessage>().ToTable("DirectMessages");
            m.Entity<DirectMessage>().HasRequired(x => x.Conversation).WithMany().HasForeignKey(x => x.ConversationId).WillCascadeOnDelete(false);
            m.Entity<DirectMessage>().HasRequired(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).WillCascadeOnDelete(false);
            m.Entity<DirectMessage>().HasIndex(x => new { x.ConversationId, x.Id });
            m.Entity<WalletTopup>().ToTable("WalletTopups");
            m.Entity<WalletTopup>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).WillCascadeOnDelete(false);
            m.Entity<WalletTopup>().Property(x => x.Amount).HasPrecision(18, 0);
            m.Entity<WalletTopup>().HasIndex(x => x.TxnRef).IsUnique();
            m.Entity<UserNotification>().HasOptional(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).WillCascadeOnDelete(false);
            m.Entity<UserNotification>().HasOptional(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).WillCascadeOnDelete(false);
            m.Entity<ExternalCertification>().HasOptional(x => x.Skill).WithMany().HasForeignKey(x => x.SkillTrackId).WillCascadeOnDelete(false);
        }
    }
}
