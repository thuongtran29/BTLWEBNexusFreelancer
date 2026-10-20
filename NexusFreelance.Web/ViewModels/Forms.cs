using System;
using System.ComponentModel.DataAnnotations;

namespace NexusFreelance.Web.ViewModels
{
    public class RegisterForm
    {
        [Required, StringLength(120)] public string Name { get; set; }
        [Required, EmailAddress, StringLength(200)] public string Email { get; set; }
        [Required, StringLength(128, MinimumLength = 12), StrongPassword, DataType(DataType.Password)] public string Password { get; set; }
        [Required] public string Role { get; set; }
    }
    public class LoginForm
    {
        [RegularExpression("Client|Freelancer|Admin")] public string Role { get; set; }
        [Required, EmailAddress, StringLength(200)] public string Email { get; set; }
        [Required, StringLength(128), DataType(DataType.Password)] public string Password { get; set; }
    }
    public class ForgotPasswordForm
    {
        [RegularExpression("Client|Freelancer|Admin", ErrorMessage = "Vui lòng chọn đúng vai trò.")] public string Role { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập email đã đăng ký."), EmailAddress(ErrorMessage = "Địa chỉ email chưa đúng định dạng."), StringLength(200, ErrorMessage = "Email không được vượt quá 200 ký tự.")] public string Email { get; set; }
    }
    public class VerifyEmailForm
    {
        [Required, RegularExpression(@"[A-Za-z0-9_-]{43}"), StringLength(43)] public string Token { get; set; }
    }
    public class ResetPasswordForm
    {
        [Required, RegularExpression(@"[A-Za-z0-9_-]{43}"), StringLength(43)] public string Token { get; set; }
        [Required, StringLength(128, MinimumLength = 12), StrongPassword, DataType(DataType.Password)] public string Password { get; set; }
        [Required, Compare("Password"), DataType(DataType.Password)] public string ConfirmPassword { get; set; }
    }
    public class ProjectAbuseReportForm
    {
        public int ProjectId { get; set; }
        [Required, StringLength(24)] public string Category { get; set; }
        [Required, StringLength(2000, MinimumLength = 15)] public string Details { get; set; }
    }

    public class AdminReviewReportForm
    {
        public int Id { get; set; }
        [Required, StringLength(16)] public string Decision { get; set; }
        [Required, StringLength(200, MinimumLength = 5)] public string Resolution { get; set; }
    }
    public class ProjectForm
    {
        [StringLength(64)] public string EditVersion { get; set; }
        public int[] SkillIds { get; set; } = new int[0];
        [Range(1,5)] public int MinimumSkillLevel { get; set; } = 1;
        [StringLength(80)] public string Industry { get; set; }
        [Required, StringLength(180)] public string Title { get; set; }
        [Required, StringLength(4000)] public string Description { get; set; }
        [Required] public string ProjectType { get; set; }
        [Range(typeof(decimal), "1", "999999999999")] public decimal Budget { get; set; }
        [DataType(DataType.Date)] public DateTime? Deadline { get; set; }
    }
    public class RequirementForm
    {
        public int ProjectId { get; set; }
        [Required, StringLength(100)] public string Title { get; set; }
        [Range(1, 50)] public int RequiredCount { get; set; }
    }
    public class ProposalForm
    {
        [Range(1,3650)] public int DeliveryDays { get; set; } = 14;
        [StringLength(4000)] public string MilestonePlan { get; set; }
        public int ProjectId { get; set; }
        public int RoleRequirementId { get; set; }
        [Required, StringLength(2500)] public string Pitch { get; set; }
        [Range(typeof(decimal), "0", "999999999999")] public decimal QuotedPrice { get; set; }
    }
    public class TaskForm
    {
        public int WorkspaceId { get; set; }
        [Required, StringLength(200)] public string Title { get; set; }
        public int AssigneeId { get; set; }
        [StringLength(12)] public string Priority { get; set; }
        public DateTime? DueAtUtc { get; set; }
    }
    public class TaskEditForm
    {
        public int Id { get; set; }
        [Required, StringLength(200)] public string Title { get; set; }
        public int AssigneeId { get; set; }
        [Required, StringLength(12)] public string Priority { get; set; }
        public DateTime? DueAtUtc { get; set; }
    }
    public class WorkspaceMessageForm
    {
        public int WorkspaceId { get; set; }
        [Required, StringLength(2000)] public string Body { get; set; }
    }
    public class MilestoneForm
    {
        public int WorkspaceId { get; set; }
        [Required, StringLength(180)] public string Title { get; set; }
        [Required] public DateTime? DueAtUtc { get; set; }
    }
    public class MeetingForm
    {
        public int WorkspaceId { get; set; }
        [Required, StringLength(180)] public string Title { get; set; }
        [Required, Url, StringLength(1000)] public string Url { get; set; }
        [Required] public DateTime? StartsAtUtc { get; set; }
    }
    public class DeliverableForm
    {
        public int? EscrowMilestoneId { get; set; }
        public int WorkspaceId { get; set; }
        public int? RevisesId { get; set; }
        [Required, StringLength(200)] public string Title { get; set; }
        [Required, Url, StringLength(1000)] public string Url { get; set; }
    }
    public class WorkspaceFileUploadForm
    {
        public int WorkspaceId { get; set; }
        public int? FileId { get; set; }
    }
    public class WorkspaceFileRow
    {
        public int FileId { get; set; }
        public int VersionId { get; set; }
        public int VersionNumber { get; set; }
        public bool IsLatest { get; set; }
        public string FileName { get; set; }
        public string UploadedByName { get; set; }
        public int ByteLength { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
    public class ReviewForm
    {
        public int ProjectId { get; set; }
        public int FreelancerId { get; set; }
        [Range(1, 5)] public int Rating { get; set; }
        [StringLength(1500)] public string Comment { get; set; }
    }
}
