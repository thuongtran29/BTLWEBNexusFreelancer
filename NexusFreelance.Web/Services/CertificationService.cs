using System;
using System.Data;
using System.Data.Entity;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Services
{
    public sealed class CertificationService
    {
        private readonly AppDbContext db;
        public CertificationService(AppDbContext db) { this.db = db; }

        public void Submit(int freelancerId, SubmitCertificationForm form)
        {
            if (form == null || !form.IssuedOn.HasValue ||
                string.IsNullOrWhiteSpace(form.Name) || string.IsNullOrWhiteSpace(form.Issuer))
                throw new InvalidOperationException("Thông tin chứng chỉ chưa đầy đủ.");
            if (!IsHttps(form.EvidenceUrl))
                throw new InvalidOperationException("Đường dẫn minh chứng phải sử dụng HTTPS.");
            var issued = form.IssuedOn.Value.Date;
            var expires = form.ExpiresOn?.Date;
            // Issued/expiry inputs are calendar dates in the site's local timezone.
            if (issued > DateTime.Today || issued.Year < 1900 ||
                (expires.HasValue && expires.Value <= issued))
                throw new InvalidOperationException("Ngày cấp hoặc hết hạn không hợp lệ.");
            if (!db.Users.Any(u => u.Id == freelancerId && u.Role == "Freelancer" && !u.IsSuspended))
                throw new InvalidOperationException("Bạn không có quyền gửi chứng chỉ.");
            if (form.SkillTrackId.HasValue && !db.SkillTracks.Any(t => t.Id == form.SkillTrackId && t.IsActive != false)) throw new InvalidOperationException("Kỹ năng không hợp lệ.");
            db.ExternalCertifications.Add(new ExternalCertification
            {
                UserAccountId = freelancerId, SkillTrackId = form.SkillTrackId,
                Name = form.Name.Trim(), Issuer = form.Issuer.Trim(),
                EvidenceUrl = form.EvidenceUrl.Trim(), IssuedOn = issued, ExpiresOn = expires,
                Status = "Pending", SubmittedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        public bool Review(int adminId, CertificationReviewForm form)
        {
            if (form == null || (form.Decision != "Verified" && form.Decision != "Rejected") ||
                string.IsNullOrWhiteSpace(form.Reason) || form.Reason.Trim().Length < 5)
                throw new InvalidOperationException("Quyết định hoặc lý do không hợp lệ.");
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                var cert = db.ExternalCertifications.Include(x => x.User)
                    .SingleOrDefault(x => x.Id == form.Id && x.Status == "Pending");
                if (cert == null) return false;
                if (!db.Users.Any(u => u.Id == adminId && u.Role == "Admin" && !u.IsSuspended))
                    throw new InvalidOperationException("Tài khoản không có quyền kiểm duyệt.");
                var skillId = form.SkillTrackId ?? cert.SkillTrackId;
                if (form.Decision == "Verified" && (!skillId.HasValue || !db.SkillTracks.Any(t => t.Id == skillId && t.IsActive != false) || form.ProficiencyLevel < 1 || form.ProficiencyLevel > 5))
                    throw new InvalidOperationException("Chọn kỹ năng và mức thành thạo phù hợp bằng chứng trước khi xác minh.");
                // Use a guarded update to ensure only one reviewer wins a concurrent request.
                var changed = db.Database.ExecuteSqlCommand(
                    "UPDATE dbo.ExternalCertifications SET [Status]=@p0, [ReviewReason]=@p1, " +
                    "[ReviewedById]=@p2, [ReviewedAtUtc]=@p3, [SkillTrackId]=@p5, [ProficiencyLevel]=@p6 WHERE [Id]=@p4 AND [Status]='Pending'",
                    form.Decision, form.Reason.Trim(), adminId, DateTime.UtcNow, form.Id, (object)skillId ?? DBNull.Value, form.Decision == "Verified" ? (object)form.ProficiencyLevel : DBNull.Value);
                if (changed != 1) { tx.Rollback(); return false; }
                var auditReason = "Certification #" + cert.Id + ": " + form.Reason.Trim();
                db.AdminAuditLogs.Add(new AdminAuditLog
                {
                    ActorId = adminId, TargetUserId = cert.UserAccountId,
                    Action = form.Decision == "Verified" ? "VerifyCertification" : "RejectCertification",
                    Reason = auditReason.Substring(0, Math.Min(200, auditReason.Length)),
                    CreatedAtUtc = DateTime.UtcNow
                });
                NotificationService.Queue(db, cert.UserAccountId, "Kết quả xác minh chứng chỉ: " + cert.Name.Substring(0, Math.Min(150, cert.Name.Length)), "/Skills/Certifications", adminId);
                db.SaveChanges();
                tx.Commit();
                return true;
            }
        }

        public static bool IsHttps(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(uri.Host) &&
                uri.UserInfo.Length == 0;
        }

        public void Classify(int adminId, int certificateId, int skillId, int level, string reason)
        {
            if (level < 1 || level > 5 || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5 || reason.Length > 300)
                throw new InvalidOperationException("Chọn kỹ năng, mức 1–5 và ghi căn cứ phân loại ít nhất 5 ký tự.");
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                if (!db.Users.Any(u => u.Id == adminId && u.Role == "Admin" && !u.IsSuspended)) throw new InvalidOperationException("Chỉ quản trị được phân loại chứng chỉ.");
                var certificate = db.ExternalCertifications.SingleOrDefault(c => c.Id == certificateId && c.Status == "Verified");
                if (certificate == null || !db.SkillTracks.Any(s => s.Id == skillId && s.IsActive != false)) throw new InvalidOperationException("Chứng chỉ phải đã xác minh và kỹ năng phải còn hoạt động.");
                certificate.SkillTrackId = skillId; certificate.ProficiencyLevel = level;
                var auditReason = "Certificate #" + certificate.Id + ": " + skillId + "/" + level + ". " + reason.Trim();
                db.AdminAuditLogs.Add(new AdminAuditLog { ActorId = adminId, TargetUserId = certificate.UserAccountId, Action = "ClassifyCertification", Reason = auditReason.Substring(0, Math.Min(200, auditReason.Length)), CreatedAtUtc = DateTime.UtcNow });
                NotificationService.Queue(db, certificate.UserAccountId, "Chứng chỉ đã được cập nhật kỹ năng và mức thành thạo", "/Skills/Certifications", adminId);
                db.SaveChanges(); tx.Commit();
            }
        }

        public static string EffectiveStatus(ExternalCertification certificate, DateTime now)
        {
            return certificate.Status == "Verified" && certificate.ExpiresOn.HasValue &&
                certificate.ExpiresOn.Value.Date < now.Date ? "Expired" : certificate.Status;
        }
    }
}
