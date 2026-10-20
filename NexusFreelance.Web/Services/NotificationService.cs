using System;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Services
{
    public static class NotificationService
    {
        public static void QueueAdmins(AppDbContext db,string title,string path, int? actorId = null, int? projectId = null)
        {
            foreach(var id in db.Users.Where(u=>u.Role=="Admin" && !u.IsSuspended).Select(u=>u.Id).ToList()) Queue(db,id,title,path,actorId,projectId);
        }
        // Queue alongside the business change, so the same SaveChanges/transaction persists both.
        public static void Queue(AppDbContext db, int recipientId, string title, string targetPath, int? actorId = null, int? projectId = null)
        {
            if (recipientId <= 0 || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(targetPath)
                || title.Length > 200 || targetPath.Length > 250 || !targetPath.StartsWith("/", StringComparison.Ordinal)
                || targetPath.StartsWith("//", StringComparison.Ordinal))
                throw new ArgumentException("Thông báo không hợp lệ.");
            var context = System.Web.HttpContext.Current;
            if (!actorId.HasValue && context?.User?.Identity?.IsAuthenticated == true)
                actorId = AuthSessionService.AccountId(new System.Web.HttpRequestWrapper(context.Request));
            int targetId;
            var parts = targetPath.Split('?')[0].Trim('/').Split('/');
            var source = parts[0];
            if (!projectId.HasValue && parts.Length >= 3 && int.TryParse(parts[2], out targetId))
            {
                if (source == "Projects") projectId = db.Projects.Where(p => p.Id == targetId).Select(p => (int?)p.Id).SingleOrDefault();
                if (source == "Workspace") projectId = db.Workspaces.Where(w => w.Id == targetId).Select(w => (int?)w.ProjectId).SingleOrDefault();
                if (source == "Contracts") projectId = db.Contracts.Where(c => c.Id == targetId).Select(c => (int?)c.ProjectId).SingleOrDefault();
            }
            db.Notifications.Add(new UserNotification
            {
                RecipientId = recipientId, Title = title,
                TargetPath = targetPath, CreatedAtUtc = DateTime.UtcNow,
                ActorId = actorId, ProjectId = projectId,
                Source = source == "Groups" ? "Nhóm freelancer" : source == "Workspace" ? "Kênh làm việc" : source == "Contracts" ? "Hợp đồng" : source == "Wallet" || source == "Payments" ? "Ví & thanh toán" : source == "Messages" ? "Hội thoại" : source == "Projects" ? "Dự án" : source == "Skills" ? "Chứng chỉ & kỹ năng" : source == "Admin" ? "Kiểm duyệt & hỗ trợ" : "Hệ thống"
            });
        }
    }
}
