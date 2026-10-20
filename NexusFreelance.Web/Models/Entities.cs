using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.Models
{
    public class UserAccount
    {
        public int Id { get; set; }
        [Required, StringLength(120)] public string Name { get; set; }
        [Required, StringLength(200)] public string Email { get; set; }
        [Required, StringLength(400)] public string PasswordHash { get; set; }
        [Required, StringLength(20)] public string Role { get; set; }
        [StringLength(200)] public string GoogleSubject { get; set; }
        public DateTime JoinedAtUtc { get; set; }
        public bool IsSuspended { get; set; }
        public DateTime? PasswordChangedAtUtc { get; set; }
        public DateTime? EmailVerifiedAtUtc { get; set; }
    }

    public class PasswordResetToken
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        [Required, StringLength(64)] public string TokenHash { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? ConsumedAtUtc { get; set; }
    }

    public class EmailVerificationToken
    {
        public int Id { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        [Required, StringLength(64)] public string TokenHash { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? ConsumedAtUtc { get; set; }
    }

    public class AdminAuditLog
    {
        public int Id { get; set; }
        public int ActorId { get; set; }
        public virtual UserAccount Actor { get; set; }
        public int TargetUserId { get; set; }
        public virtual UserAccount TargetUser { get; set; }
        [Required, StringLength(40)] public string Action { get; set; }
        [Required, StringLength(200)] public string Reason { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class ProjectAbuseReport
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int ReporterId { get; set; }
        public virtual UserAccount Reporter { get; set; }
        public int? ReviewerId { get; set; }
        public virtual UserAccount Reviewer { get; set; }
        [Required, StringLength(24)] public string Category { get; set; }
        [Required, StringLength(2000)] public string Details { get; set; }
        [Required, StringLength(16)] public string Status { get; set; }
        [StringLength(200)] public string Resolution { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
    }

    public class Project
    {
        public int Id { get; set; }
        public int ClientId { get; set; }
        public virtual UserAccount Client { get; set; }
        [Required, StringLength(180)] public string Title { get; set; }
        [Required, StringLength(4000)] public string Description { get; set; }
        [Required, StringLength(16)] public string ProjectType { get; set; }
        [Required, StringLength(18)] public string Status { get; set; }
        [StringLength(80)] public string Industry { get; set; }
        public decimal PlatformFeeRate { get; set; } = 0.10m;
        public decimal Budget { get; set; }
        public DateTime? DeadlineUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? PublishedAtUtc { get; set; }
        public virtual ICollection<RoleRequirement> Requirements { get; set; } = new List<RoleRequirement>();
    }

    public class RoleRequirement
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        [Required, StringLength(100)] public string Title { get; set; }
        [Range(1, 50)] public int RequiredCount { get; set; }
    }

    public class Proposal
    {
        [Range(1,3650)] public int DeliveryDays { get; set; } = 14;
        [StringLength(4000)] public string MilestonePlan { get; set; }
        public int Id { get; set; }
        public int? GroupBidId { get; set; }
        public virtual GroupBid GroupBid { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int RoleRequirementId { get; set; }
        public virtual RoleRequirement Requirement { get; set; }
        public int ApplicantId { get; set; }
        public virtual UserAccount Applicant { get; set; }
        [Required, StringLength(2500)] public string Pitch { get; set; }
        public decimal QuotedPrice { get; set; }
        [Required, StringLength(16)] public string Status { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class RoleAssignment
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int RoleRequirementId { get; set; }
        public virtual RoleRequirement Requirement { get; set; }
        public int FreelancerId { get; set; }
        public virtual UserAccount Freelancer { get; set; }
        public int ProposalId { get; set; }
        public virtual Proposal Proposal { get; set; }
        public DateTime AssignedAtUtc { get; set; }
    }

    public class ProjectTeam
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public bool Locked { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public virtual ICollection<TeamMember> Members { get; set; } = new List<TeamMember>();
    }

    public class TeamMember
    {
        public int Id { get; set; }
        public int ProjectTeamId { get; set; }
        public virtual ProjectTeam Team { get; set; }
        public int UserAccountId { get; set; }
        public virtual UserAccount User { get; set; }
        public int RoleAssignmentId { get; set; }
        public virtual RoleAssignment Assignment { get; set; }
    }

    public class Workspace
    {
        public int? ContractId { get; set; }
        public virtual ProjectContract Contract { get; set; }
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int ProjectTeamId { get; set; }
        public virtual ProjectTeam Team { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class WorkTask
    {
        public int Id { get; set; }
        public int WorkspaceId { get; set; }
        public virtual Workspace Workspace { get; set; }
        public int AssigneeId { get; set; }
        public virtual UserAccount Assignee { get; set; }
        [Required, StringLength(200)] public string Title { get; set; }
        [Required, StringLength(12)] public string Status { get; set; }
        public DateTime? DueAtUtc { get; set; }
        // NULL on legacy tasks is displayed as Normal. New tasks always have a priority.
        [StringLength(12)] public string Priority { get; set; }
    }

    public class TaskHistory
    {
        public int Id { get; set; }
        public int WorkTaskId { get; set; }
        public virtual WorkTask Task { get; set; }
        public int ActorId { get; set; }
        public virtual UserAccount Actor { get; set; }
        [Required, StringLength(24)] public string EventType { get; set; }
        [StringLength(12)] public string OldStatus { get; set; }
        [Required, StringLength(12)] public string NewStatus { get; set; }
        // Field snapshots preserve each edit independently of later task changes.
        [StringLength(200)] public string OldTitle { get; set; }
        [StringLength(200)] public string NewTitle { get; set; }
        public int? OldAssigneeId { get; set; }
        public int? NewAssigneeId { get; set; }
        [StringLength(12)] public string OldPriority { get; set; }
        [StringLength(12)] public string NewPriority { get; set; }
        public DateTime? OldDueAtUtc { get; set; }
        public DateTime? NewDueAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    // Persistent project discussion, visible only to the Client and current project members.
    public class WorkspaceMessage
    {
        public int Id { get; set; }
        public int WorkspaceId { get; set; }
        public virtual Workspace Workspace { get; set; }
        public int AuthorId { get; set; }
        public virtual UserAccount Author { get; set; }
        [Required, StringLength(2000)] public string Body { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    // Workspace schedule remains private to the project's Client and accepted members.
    public class ProjectMilestone
    {
        public int Id { get; set; }
        public int WorkspaceId { get; set; }
        public virtual Workspace Workspace { get; set; }
        [Required, StringLength(180)] public string Title { get; set; }
        public DateTime DueAtUtc { get; set; }
        [Required, StringLength(12)] public string Status { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    public class WorkspaceMeeting
    {
        public int Id { get; set; }
        public int WorkspaceId { get; set; }
        public virtual Workspace Workspace { get; set; }
        [Required, StringLength(180)] public string Title { get; set; }
        [Required, StringLength(1000)] public string Url { get; set; }
        public DateTime StartsAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class Deliverable
    {
        public int? EscrowMilestoneId { get; set; }
        public virtual EscrowMilestone EscrowMilestone { get; set; }
        public int Id { get; set; }
        public int WorkspaceId { get; set; }
        public virtual Workspace Workspace { get; set; }
        public int SubmittedById { get; set; }
        public virtual UserAccount SubmittedBy { get; set; }
        // A new submission points to the rejected version it replaces.
        // Historical versions remain available for review/audit.
        public int? RevisesId { get; set; }
        public virtual Deliverable RevisedFrom { get; set; }
        [Required, StringLength(200)] public string Title { get; set; }
        [Required, StringLength(1000)] public string Url { get; set; }
        [Required, StringLength(24)] public string Status { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    // Workspace files are stored in SQL Server with immutable content versions.
    // Each version remains available to the client and assigned team members.
    public class WorkspaceFile
    {
        public int Id { get; set; }
        public int WorkspaceId { get; set; }
        public virtual Workspace Workspace { get; set; }
        public int CreatedById { get; set; }
        public virtual UserAccount CreatedBy { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class WorkspaceFileVersion
    {
        public int Id { get; set; }
        public int WorkspaceFileId { get; set; }
        public virtual WorkspaceFile File { get; set; }
        public int VersionNumber { get; set; }
        public int UploadedById { get; set; }
        public virtual UserAccount UploadedBy { get; set; }
        [Required, StringLength(180)] public string FileName { get; set; }
        [Required, StringLength(100)] public string ContentType { get; set; }
        [Required, StringLength(64)] public string Sha256 { get; set; }
        public int ByteLength { get; set; }
        [Required] public byte[] Data { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class Review
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int ClientId { get; set; }
        public virtual UserAccount Client { get; set; }
        public int FreelancerId { get; set; }
        public virtual UserAccount Freelancer { get; set; }
        [Range(1, 5)] public int Rating { get; set; }
        [StringLength(1500)] public string Comment { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class ActivityLog
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public virtual Project Project { get; set; }
        public int ActorId { get; set; }
        public virtual UserAccount Actor { get; set; }
        [Required, StringLength(200)] public string Action { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
