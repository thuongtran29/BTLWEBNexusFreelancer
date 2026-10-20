using System.Data.Entity;
using NexusFreelance.Web.Models;
namespace NexusFreelance.Web.Data
{
 public static class KnowledgeRecruitmentModelConfig
 {
  public static void Configure(DbModelBuilder m) {
   m.Entity<SharedKnowledgeEntry>().ToTable("SharedKnowledgeEntries");
   m.Entity<SharedKnowledgeEntry>().HasRequired(x=>x.Project).WithMany().HasForeignKey(x=>x.ProjectId).WillCascadeOnDelete(false);
   m.Entity<SharedKnowledgeEntry>().HasRequired(x=>x.Author).WithMany().HasForeignKey(x=>x.AuthorId).WillCascadeOnDelete(false);
   m.Entity<SharedKnowledgeEntry>().HasIndex(x=>new{x.ProjectId,x.Version}).IsUnique();
   m.Entity<GroupProjectRecruitment>().ToTable("GroupProjectRecruitments");
   m.Entity<GroupProjectRecruitment>().HasRequired(x=>x.Project).WithMany().HasForeignKey(x=>x.ProjectId).WillCascadeOnDelete(false);
   m.Entity<GroupProjectRecruitment>().HasRequired(x=>x.Group).WithMany().HasForeignKey(x=>x.FreelancerGroupId).WillCascadeOnDelete(false);
   m.Entity<GroupProjectRecruitment>().HasIndex(x=>new{x.FreelancerGroupId,x.ProjectId}).IsUnique();
   m.Entity<GroupJoinRequest>().ToTable("GroupJoinRequests");
   m.Entity<GroupJoinRequest>().HasRequired(x=>x.Recruitment).WithMany().HasForeignKey(x=>x.GroupProjectRecruitmentId).WillCascadeOnDelete(false);
   m.Entity<GroupJoinRequest>().HasRequired(x=>x.Freelancer).WithMany().HasForeignKey(x=>x.FreelancerId).WillCascadeOnDelete(false);
   m.Entity<GroupJoinRequest>().HasRequired(x=>x.Role).WithMany().HasForeignKey(x=>x.RoleRequirementId).WillCascadeOnDelete(false);
   m.Entity<GroupJoinRequest>().HasIndex(x=>new{x.GroupProjectRecruitmentId,x.FreelancerId}).IsUnique();
  }
 }
}
