using System;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Text.RegularExpressions;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;
namespace NexusFreelance.Web.Services
{
 public sealed class SharedKnowledgeService
 {
  private readonly AppDbContext db;
  public SharedKnowledgeService(AppDbContext context){db=context;}
  public IQueryable<SharedKnowledgeEntry> Available()=>db.SharedKnowledgeEntries.AsNoTracking().Where(k=>k.Status=="Published" && k.Project.Status=="Completed" && !k.Author.IsSuspended);
  public static bool ContainsPrivateMarkers(string value)=>Regex.IsMatch(value??"",@"(?i)(https?://|www\.|[\w.+-]+@[\w.-]+\.[a-z]{2,}|(?:\+?\d[\s().-]*){8,}|password\s*[:=]|mật khẩu\s*[:=]|api[_ -]?key\s*[:=])");
  public static void Validate(KnowledgePublishForm f) {
   if(f==null || !f.ConfirmGeneric)throw new InvalidOperationException("Xác nhận nội dung chỉ gồm kinh nghiệm chung trước khi chia sẻ.");
   var fields=new[]{f.Title,f.Summary,f.Practices,f.AcceptanceChecklist};var min=new[]{10,30,30,30};var max=new[]{180,800,1500,1200};
   for(int i=0;i<fields.Length;i++)if(string.IsNullOrWhiteSpace(fields[i]) || fields[i].Trim().Length<min[i] || fields[i].Length>max[i])throw new InvalidOperationException("Điền đủ nội dung chung và giữ trong giới hạn của từng ô.");
   if((f.SkillTags??"").Length>400 || ContainsPrivateMarkers(string.Join("\n",fields.Concat(new[]{f.SkillTags}))))throw new InvalidOperationException("Bỏ email, số liên hệ/tài khoản, liên kết và thông tin đăng nhập khỏi nội dung chia sẻ. Chỉ giữ bài học và tiêu chí chung.");
  }
  public int Publish(int actorId,KnowledgePublishForm f) {
   Validate(f);
   using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted)) {
    var p=db.Projects.SqlQuery("SELECT * FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0",f.ProjectId).SingleOrDefault();
    if(p==null || p.ClientId!=actorId || p.Status!="Completed")throw new InvalidOperationException("Chỉ chủ dự án đã hoàn thành được chia sẻ bài học chung.");
    var latest=db.SharedKnowledgeEntries.Where(k=>k.ProjectId==p.Id).OrderByDescending(k=>k.Version).FirstOrDefault();
    if((latest?.Version??0)!=f.ExpectedVersion)throw new InvalidOperationException("Bài học đã có phiên bản mới. Hãy tải lại trước khi lưu.");
    foreach(var old in db.SharedKnowledgeEntries.Where(k=>k.ProjectId==p.Id && k.Status=="Published")){old.Status="Withdrawn";old.WithdrawnAtUtc=DateTime.UtcNow;}
    var item=new SharedKnowledgeEntry {ProjectId=p.Id,AuthorId=actorId,Version=f.ExpectedVersion+1,Title=f.Title.Trim(),Industry=IndustryCatalog.Normalize(p.Industry),Summary=f.Summary.Trim(),Practices=f.Practices.Trim(),AcceptanceChecklist=f.AcceptanceChecklist.Trim(),SkillTags=(f.SkillTags??"").Trim(),Status="Published",CreatedAtUtc=DateTime.UtcNow};
    db.SharedKnowledgeEntries.Add(item);db.ActivityLogs.Add(new ActivityLog {ProjectId=p.Id,ActorId=actorId,Action="Published generic knowledge v"+item.Version,CreatedAtUtc=DateTime.UtcNow});db.SaveChanges();tx.Commit();return item.Id;
   }
  }
  public void Withdraw(int id,int actorId,string reason) {
   using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted)) {
    var projectId=db.SharedKnowledgeEntries.Where(row=>row.Id==id).Select(row=>(int?)row.ProjectId).SingleOrDefault();if(!projectId.HasValue)throw new InvalidOperationException("Bài học không tồn tại.");
    db.Database.SqlQuery<int>("SELECT Id FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0",projectId).Single();
    var k=db.SharedKnowledgeEntries.Single(x=>x.Id==id);var actor=db.Users.Single(x=>x.Id==actorId);
    if(k.AuthorId!=actorId && actor.Role!="Admin")throw new UnauthorizedAccessException();
    if(string.IsNullOrWhiteSpace(reason) || reason.Trim().Length<5 || reason.Length>200)throw new InvalidOperationException("Ghi lý do ngừng chia sẻ từ 5 đến 200 ký tự.");
    if(k.Status=="Published"){k.Status="Withdrawn";k.WithdrawnAtUtc=DateTime.UtcNow;db.ActivityLogs.Add(new ActivityLog{ProjectId=k.ProjectId,ActorId=actorId,Action="Withdrew shared knowledge #"+id+": "+reason.Trim().Substring(0,Math.Min(140,reason.Trim().Length)),CreatedAtUtc=DateTime.UtcNow});db.SaveChanges();}tx.Commit();
   }
  }
 }
}

