using System;
using System.ComponentModel.DataAnnotations;
namespace NexusFreelance.Web.Models
{
    // Explicitly authored generic knowledge. Private ProjectMemory is never auto-published.
    public class SharedKnowledgeEntry
    {
        public int Id {get;set;}
        public int ProjectId {get;set;}
        public virtual Project Project {get;set;}
        public int AuthorId {get;set;}
        public virtual UserAccount Author {get;set;}
        public int Version {get;set;}
        [Required,StringLength(180)] public string Title {get;set;}
        [Required,StringLength(80)] public string Industry {get;set;}
        [Required,StringLength(800)] public string Summary {get;set;}
        [Required,StringLength(1500)] public string Practices {get;set;}
        [Required,StringLength(1200)] public string AcceptanceChecklist {get;set;}
        [StringLength(400)] public string SkillTags {get;set;}
        [Required,StringLength(16)] public string Status {get;set;}
        public DateTime CreatedAtUtc {get;set;}
        public DateTime? WithdrawnAtUtc {get;set;}
    }
    public class GroupProjectRecruitment
    {
        public int Id {get;set;}
        public int FreelancerGroupId {get;set;}
        public virtual FreelancerGroup Group {get;set;}
        public int ProjectId {get;set;}
        public virtual Project Project {get;set;}
        [Required,StringLength(12)] public string Status {get;set;}
        public DateTime CreatedAtUtc {get;set;}
        public DateTime? ClosedAtUtc {get;set;}
    }
    public class GroupJoinRequest
    {
        public int Id {get;set;}
        public int GroupProjectRecruitmentId {get;set;}
        public virtual GroupProjectRecruitment Recruitment {get;set;}
        public int FreelancerId {get;set;}
        public virtual UserAccount Freelancer {get;set;}
        public int RoleRequirementId {get;set;}
        public virtual RoleRequirement Role {get;set;}
        [Required,StringLength(600)] public string Pitch {get;set;}
        [Required,StringLength(12)] public string Status {get;set;}
        public DateTime CreatedAtUtc {get;set;}
        public DateTime? DecidedAtUtc {get;set;}
    }
}
