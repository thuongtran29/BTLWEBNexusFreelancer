using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Entity;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
namespace NexusFreelance.Web.Services
{
    public class EvidenceInput
    {
        [StringLength(16)] public string Type { get; set; }
        [StringLength(1000)] public string Url { get; set; }
        [StringLength(200)] public string Issuer { get; set; }
        public DateTime? IssuedOn { get; set; }
        public DateTime? ExpiresOn { get; set; }
        public int? SkillExamAttemptId { get; set; }
        public int? PortfolioItemId { get; set; }
    }
    public class SkillEvidenceService
    {
        private readonly AppDbContext db;
        public SkillEvidenceService(AppDbContext db) { this.db = db; }
        private static void Must(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        private SkillClaim Lock(int id)
        {
            var c = db.SkillClaims.SqlQuery("SELECT * FROM dbo.SkillClaims WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0",id).SingleOrDefault();
            Must(c != null, "Kỹ năng không tồn tại."); db.Entry(c).Reload(); return c;
        }
        public void SaveClaim(int userId, int skillId, int level, int years)
        {
            Must(level >= 1 && level <= 5 && years >= 0 && years <= 60, "Mức độ 1–5 và kinh nghiệm 0–60 năm.");
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                db.Database.SqlQuery<int>("SELECT Id FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0",userId).Single();
                Must(db.SkillTracks.Any(s => s.Id == skillId && s.IsActive != false), "Kỹ năng không còn được sử dụng.");
                var c = db.SkillClaims.SingleOrDefault(x => x.UserAccountId == userId && x.SkillTrackId == skillId);
                if (c == null) { c = new SkillClaim { UserAccountId = userId, SkillTrackId = skillId }; db.SkillClaims.Add(c); }
                c.Level = level; c.YearsExperience = years; c.IsDeleted = false; db.SaveChanges(); tx.Commit();
            }
        }
        public void DeleteClaim(int id, int userId)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var c=Lock(id); Must(c.UserAccountId==userId,"Không có quyền sửa kỹ năng."); c.IsDeleted=true;
                foreach(var r in db.SkillEvidenceRequests.Where(r=>r.SkillClaimId==id && (r.Status=="Pending" || r.Status=="Approved"))) r.Status="Cancelled";
                db.SaveChanges();tx.Commit();
            }
        }
        public void Submit(int claimId, int userId, List<EvidenceInput> inputs)
        {
            inputs=(inputs??new List<EvidenceInput>()).Where(x=>x!=null && !string.IsNullOrWhiteSpace(x.Type)).ToList();
            Must(inputs.Count>=1 && inputs.Count<=5,"Mỗi yêu cầu cần 1–5 bằng chứng.");
            using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var c=Lock(claimId);Must(c.UserAccountId==userId && !c.IsDeleted,"Kỹ năng không thuộc hồ sơ hiện tại.");
                Must(!db.SkillEvidenceRequests.Any(r=>r.SkillClaimId==claimId && r.Status=="Pending"),"Kỹ năng đã có yêu cầu đang chờ.");
                var last=db.SkillEvidenceRequests.Where(r=>r.SkillClaimId==claimId && r.Status=="Rejected").OrderByDescending(r=>r.ReviewedAtUtc).FirstOrDefault();
                int days; if(!int.TryParse(Environment.GetEnvironmentVariable("NEXUS_SKILL_RETRY_DAYS"),out days)||days<0||days>30)days=3;
                Must(last==null || !last.ReviewedAtUtc.HasValue || last.ReviewedAtUtc.Value.AddDays(days)<=DateTime.UtcNow,"Cần chờ "+days+" ngày sau lần bị từ chối trước khi nộp lại.");
                var request=new SkillEvidenceRequest { SkillClaimId=claimId, Status="Pending",SubmittedAtUtc=DateTime.UtcNow };
                var evidence=new List<SkillEvidence>();
                foreach(var x in inputs)
                {
                    var e=new SkillEvidence {Request=request,Type=x.Type};
                    if(x.Type=="Certificate")
                    {
                        Uri url;Must(x.Url!=null && x.Url.Length<=1000 && Uri.TryCreate(x.Url,UriKind.Absolute,out url)&&url.Scheme=="https","Bằng chứng cần URL HTTPS.");
                        Must(!string.IsNullOrWhiteSpace(x.Issuer)&&x.Issuer.Length<=200 && x.IssuedOn.HasValue && x.IssuedOn.Value.Date<=DateTime.Today && x.IssuedOn.Value.Year>=1900,"Chứng chỉ cần tổ chức cấp và ngày cấp hợp lệ.");
                        Must(!x.ExpiresOn.HasValue || (x.ExpiresOn.Value.Date>x.IssuedOn.Value.Date && x.ExpiresOn.Value.Date>=DateTime.Today),"Chứng chỉ đã hết hạn hoặc ngày hết hạn không hợp lệ.");
                        e.Url=x.Url.Trim();e.Issuer=x.Issuer.Trim();e.IssuedOn=x.IssuedOn.Value.Date;e.ExpiresOn=x.ExpiresOn?.Date;
                    }
                    else if(x.Type=="Test")
                    {
                        var attempt=db.SkillExamAttempts.SingleOrDefault(a=>a.Id==x.SkillExamAttemptId && a.UserAccountId==userId && a.SkillTrackId==c.SkillTrackId);
                        Must(attempt!=null,"Bài kiểm tra phải thuộc bạn và đúng kỹ năng.");e.SkillExamAttemptId=attempt.Id;e.Score=attempt.Percent;
                    }
                    else if(x.Type=="Portfolio")
                    {
                        var item=db.PortfolioItems.SingleOrDefault(p=>p.Id==x.PortfolioItemId && p.FreelancerId==userId);Must(item!=null,"Portfolio không thuộc bạn.");
                        Uri link; Must(Uri.TryCreate(item.Url,UriKind.Absolute,out link) && link.Scheme=="https","Portfolio cần liên kết HTTPS hợp lệ.");
                        e.PortfolioItemId=item.Id;e.Url=item.Url;
                    }
                    else throw new InvalidOperationException("Loại bằng chứng không hợp lệ.");
                    evidence.Add(e);
                }
                var expiries=evidence.Where(e=>e.ExpiresOn.HasValue).Select(e=>e.ExpiresOn.Value).ToList();request.ValidUntil=expiries.Count>0?(DateTime?)expiries.Min():null;
                db.SkillEvidenceRequests.Add(request);db.SkillEvidence.AddRange(evidence);
                NotificationService.QueueAdmins(db,"Có yêu cầu xác minh kỹ năng mới","/SkillEvidence/Admin");db.SaveChanges();tx.Commit();
            }
        }
        public void Review(int id,int adminId,bool approve,string reason)
        {
            Must(reason!=null && reason.Trim().Length>=5 && reason.Length<=300,"Nhập lý do 5–300 ký tự.");
            var owner=db.SkillEvidenceRequests.Where(r=>r.Id==id).Select(r=>(int?)r.SkillClaimId).SingleOrDefault();Must(owner.HasValue,"Yêu cầu không tồn tại.");
            using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                Must(db.Users.Any(u=>u.Id==adminId && u.Role=="Admin" && !u.IsSuspended),"Chỉ quản trị được xác minh.");
                var c=Lock(owner.Value);var r=db.SkillEvidenceRequests.Single(x=>x.Id==id);db.Entry(r).Reload();
                Must(!c.IsDeleted && r.Status=="Pending" && db.SkillEvidence.Any(e=>e.SkillEvidenceRequestId==id),"Yêu cầu đã xử lý hoặc không có bằng chứng.");
                Must(!approve || !r.ValidUntil.HasValue || r.ValidUntil.Value.Date>=DateTime.Today,"Bằng chứng đã hết hạn.");
                r.Status=approve?"Approved":"Rejected";r.ReviewerId=adminId;r.ReviewedAtUtc=DateTime.UtcNow;r.Reason=reason.Trim();
                db.AdminAuditLogs.Add(new AdminAuditLog{ActorId=adminId,TargetUserId=c.UserAccountId,Action="ReviewSkillEvidence",Reason="SkillRequest #"+id+": "+r.Status+". "+reason.Trim().Substring(0,Math.Min(reason.Trim().Length,130)),CreatedAtUtc=DateTime.UtcNow});
                NotificationService.Queue(db,c.UserAccountId,"Yêu cầu xác minh kỹ năng: "+r.Status,"/SkillEvidence");db.SaveChanges();tx.Commit();
            }
        }
        public void Expire()
        {
            var today=DateTime.Today;
            var ids=db.SkillEvidenceRequests.Where(r=>r.Status=="Approved" && r.ValidUntil<today).OrderBy(r=>r.Id).Take(50).Select(r=>r.Id).ToArray();
            foreach(var id in ids)
            using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var claimId=db.SkillEvidenceRequests.Where(x=>x.Id==id).Select(x=>x.SkillClaimId).Single();var claim=Lock(claimId);
                var r=db.SkillEvidenceRequests.Single(x=>x.Id==id);db.Entry(r).Reload();
                if(r.Status=="Approved" && r.ValidUntil<today) {r.Status="Expired";NotificationService.Queue(db,claim.UserAccountId,"Bằng chứng kỹ năng đã hết hạn, hãy cập nhật","/SkillEvidence");db.SaveChanges();}
                tx.Commit();
            }
        }
    }
}
