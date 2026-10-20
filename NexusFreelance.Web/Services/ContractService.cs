using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Services
{
    public sealed class ContractSnapshot
    {
        public string ProjectTitle { get; set; }
        public string Scope { get; set; }
        public string Currency { get; set; }
        public decimal Price { get; set; }
        public decimal FeeRate { get; set; }
        public int DeliveryDays { get; set; }
        public string AcceptancePolicy { get; set; }
        public string ProductRights { get; set; }
        public List<ContractRosterItem> Members { get; set; }
        public List<ContractPlanItem> Milestones { get; set; }
    }
    public sealed class ContractRosterItem
    {
        public int UserId { get; set; }
        public int ProposalId { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }
        public decimal Weight { get; set; }
    }
    public sealed class ContractPlanItem
    {
        public string Title { get; set; }
        public decimal Amount { get; set; }
        public int? DueDay { get; set; }
        public string AcceptanceCriteria { get; set; }
    }
    public sealed class ContractService
    {
        private readonly AppDbContext db;
        public ContractService(AppDbContext database) { db = database; }
        private static void Must(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        public static string Digest(string value)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
        }
        public static ContractSnapshot ReadSnapshot(ContractVersion version)
        {
            Must(version != null && Digest(version.SnapshotJson) == version.Hash, "Nội dung phiên bản không còn khớp mã xác nhận.");
            var snapshot=new JavaScriptSerializer().Deserialize<ContractSnapshot>(version.SnapshotJson);
            Must(snapshot.Price==version.Price && snapshot.FeeRate==version.FeeRate && snapshot.DeliveryDays==version.DeliveryDays && snapshot.Currency=="VND","Giá và điều khoản không khớp nội dung phiên bản.");
            return snapshot;
        }
        public bool CanView(ProjectContract c, int userId) => c != null && (c.Project.ClientId == userId || c.CounterpartyId == userId || db.ContractMembers.Any(m=>m.ContractId==c.Id && m.UserAccountId==userId));
        public bool CanAccess(Workspace w, int userId) => w != null && (w.ContractId.HasValue ? CanView(w.Contract ?? db.Contracts.Include(c=>c.Project).Single(c=>c.Id==w.ContractId),userId) :
            w.Project.ClientId==userId || db.TeamMembers.Any(m=>m.ProjectTeamId==w.ProjectTeamId && m.UserAccountId==userId));
        public bool IsMember(Workspace w, int userId) => w.ContractId.HasValue ? db.ContractMembers.Any(m=>m.ContractId==w.ContractId && m.UserAccountId==userId) : db.TeamMembers.Any(m=>m.ProjectTeamId==w.ProjectTeamId && m.UserAccountId==userId);
        public bool CanManageWork(Workspace w, int userId) => w != null && CanAccess(w, userId) && CanSubmit(w, userId);
        public bool CanWork(Workspace w) => w.ContractId.HasValue ? db.Contracts.Any(c=>c.Id==w.ContractId && c.Status=="Active") : w.Project.Status=="InProgress";
        public bool CanDiscuss(Workspace w) => w != null && (w.ContractId.HasValue ? db.Contracts.Any(c => c.Id == w.ContractId &&
            (c.Status == "AwaitingSignatures" || c.Status == "AwaitingEscrow" || c.Status == "Active" || c.Status == "Disputed")) :
            w.Project.Status == "AwaitingEscrow" || w.Project.Status == "InProgress" || w.Project.Status == "Disputed");
        public bool CanSubmit(Workspace w, int userId) => w.ContractId.HasValue ? db.Contracts.Any(c=>c.Id==w.ContractId && c.CounterpartyId==userId) :
            IsMember(w,userId) && (w.Project.ProjectType=="SOLO" || db.GroupBids.Any(b=>b.ProjectId==w.ProjectId && b.Status=="Accepted" && b.LeaderId==userId));
        public ContractVersion Current(ProjectContract c) => db.ContractVersions.Single(v=>v.ContractId==c.Id && v.VersionNumber==c.CurrentVersion);
        private int[] Signers(ProjectContract c) => db.ContractMembers.Where(m=>m.ContractId==c.Id).Select(m=>m.UserAccountId).ToList().Concat(new[]{c.Project.ClientId,c.CounterpartyId}).Distinct().OrderBy(x=>x).ToArray();
        private ProjectContract Lock(int id)
        {
            var projectId=db.Contracts.Where(c=>c.Id==id).Select(c=>(int?)c.ProjectId).SingleOrDefault();
            Must(projectId.HasValue,"Không tìm thấy hợp đồng.");
            db.Database.SqlQuery<int>("SELECT Id FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0",projectId.Value).Single();
            var contract=db.Contracts.Include(c=>c.Project).Single(c=>c.Id==id); db.Entry(contract).Reload(); return contract;
        }
        // Caller owns the project transaction and project update lock. Never creates signatures.
        public ProjectContract CreateSelected(Project project, IList<Proposal> proposals)
        {
            Must(db.Database.CurrentTransaction!=null,"Thiếu giao dịch chốt hợp đồng.");
            Must(proposals.Count>0 && proposals.All(p=>p.ProjectId==project.Id && p.Status=="Accepted" && WalletService.ValidAmount(p.QuotedPrice)),"Chào giá được chọn không hợp lệ.");
            var groupId=proposals[0].GroupBidId;
            Must(groupId.HasValue ? proposals.All(p=>p.GroupBidId==groupId) : proposals.Count==1,"Mỗi hợp đồng thuộc một chào giá cá nhân hoặc một chào giá nhóm.");
            var key=groupId.HasValue ? "bid:"+groupId.Value : "proposal:"+proposals[0].Id;
            var existing=db.Contracts.SingleOrDefault(c=>c.SourceKey==key); if(existing!=null) return existing;
            var client=db.Users.Single(u=>u.Id==project.ClientId);
            var ids=proposals.Select(p=>p.ApplicantId).Distinct().ToArray();
            Must(ids.Length==proposals.Count,"Đội hình trùng thành viên.");
            Must(!db.Users.Any(u=>ids.Contains(u.Id) && (u.IsSuspended || u.Role!="Freelancer" || u.Email==client.Email || (client.GoogleSubject!=null && u.GoogleSubject==client.GoogleSubject))),"Không được tự giao dịch giữa các hồ sơ cùng người hoặc nhận tài khoản không đủ điều kiện.");
            var contract=new ProjectContract {ProjectId=project.Id, ProposalId=groupId.HasValue?(int?)null:proposals[0].Id, GroupBidId=groupId,
                CounterpartyId=groupId.HasValue?db.GroupBids.Where(b=>b.Id==groupId).Select(b=>b.LeaderId).Single():proposals[0].ApplicantId,
                SourceKey=key, Status="AwaitingSignatures",CurrentVersion=1,CreatedAtUtc=DateTime.UtcNow};
            db.Contracts.Add(contract); db.SaveChanges();
            Must(ids.Contains(contract.CounterpartyId),"Người đại diện phải thuộc đội hình đã chọn.");
            var price=proposals.Sum(p=>p.QuotedPrice); Must(WalletService.ValidAmount(price),"Giá hợp đồng không hợp lệ.");
            var roster=proposals.OrderBy(p=>p.ApplicantId).Select(p=>new ContractRosterItem {UserId=p.ApplicantId, ProposalId=p.Id,
                Name=db.Users.Where(u=>u.Id==p.ApplicantId).Select(u=>u.Name).Single(),Role=db.RoleRequirements.Where(r=>r.Id==p.RoleRequirementId).Select(r=>r.Title).Single(),Weight=p.QuotedPrice}).ToList();
            foreach(var item in roster)
            {
                db.ContractMembers.Add(new ContractMember {ContractId=contract.Id,UserAccountId=item.UserId,ProposalId=item.ProposalId,Weight=item.Weight});
                db.EscrowShares.Add(new EscrowShare {ContractId=contract.Id,ProjectId=project.Id,UserAccountId=item.UserId,Weight=item.Weight});
            }
            var plan=PaymentPlan.Parse(proposals[0].MilestonePlan,price,project.Id,proposals.Max(p=>p.DeliveryDays));
            var snapshot=new ContractSnapshot {ProjectTitle=project.Title, Scope=project.Description+"\n\n"+proposals[0].Pitch,Currency="VND",Price=price,FeeRate=project.PlatformFeeRate,
                DeliveryDays=proposals.Max(p=>p.DeliveryDays),Members=roster,Milestones=plan.Select(m=>new ContractPlanItem {Title=m.Title,Amount=m.Amount,DueDay=m.DueDay,AcceptanceCriteria=m.AcceptanceCriteria}).ToList(),
                AcceptancePolicy="Giá cố định theo mốc. Chỉ bắt đầu sau đủ xác nhận và bảo đảm mốc đầu. Client nghiệm thu hoặc yêu cầu sửa; quá 7 ngày chỉ nhắc. Không tự nghiệm thu. Phân chia sau phí theo tỷ trọng đội hình đã xác nhận.",
                ProductRights="Client được sử dụng sản phẩm sau khi thanh toán đầy đủ phạm vi được nghiệm thu. Việc đưa sản phẩm/tài liệu Client vào Portfolio công khai cần chấp thuận riêng của Client."};
            AddVersion(contract,snapshot);
            foreach(var milestone in plan) {milestone.ContractId=contract.Id;db.EscrowMilestones.Add(milestone);}
            var team=db.ProjectTeams.Single(t=>t.ProjectId==project.Id);
            db.Workspaces.Add(new Workspace {ProjectId=project.Id,ProjectTeamId=team.Id,ContractId=contract.Id,CreatedAtUtc=DateTime.UtcNow});
            foreach(var signer in ids.Concat(new[]{project.ClientId}).Distinct()) NotificationService.Queue(db,signer,"Hợp đồng #"+contract.Id+" đang chờ xác nhận điều khoản","/Contracts/Details/"+contract.Id);
            db.SaveChanges(); return contract;
        }
        private void AddVersion(ProjectContract c, ContractSnapshot snapshot)
        {
            var json=new JavaScriptSerializer().Serialize(snapshot);
            db.ContractVersions.Add(new ContractVersion {ContractId=c.Id,VersionNumber=c.CurrentVersion,SnapshotJson=json,Hash=Digest(json),Price=snapshot.Price,FeeRate=snapshot.FeeRate,DeliveryDays=snapshot.DeliveryDays,CreatedAtUtc=DateTime.UtcNow});
        }
        public void EnsureSelected(Project project)
        {
            var proposals=db.RoleAssignments.Where(a=>a.ProjectId==project.Id).Select(a=>a.Proposal).ToList();
            foreach(var subset in proposals.GroupBy(p=>p.GroupBidId.HasValue?"b:"+p.GroupBidId:"p:"+p.Id)) CreateSelected(project,subset.ToList());
        }
        public void Confirm(int id,int userId,int versionId,string hash)
        {
            using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var c=Lock(id); Must(CanView(c,userId),"Bạn không phải bên xác nhận hợp đồng.");
                var v=Current(c); ReadSnapshot(v);
                Must(v.Id==versionId && v.Hash==hash,"Phiên bản đã thay đổi. Hãy đọc và xác nhận bản mới nhất.");
                var existing=db.ContractAcceptances.SingleOrDefault(a=>a.ContractVersionId==v.Id && a.UserAccountId==userId);
                if(existing!=null) {Must(existing.AcceptedHash==hash,"Mã xác nhận không khớp.");tx.Commit();return;}
                Must(c.Status=="AwaitingSignatures","Hợp đồng không còn chờ xác nhận.");
                db.ContractAcceptances.Add(new ContractAcceptance {ContractVersionId=v.Id,UserAccountId=userId,AcceptedHash=hash,AcceptedAtUtc=DateTime.UtcNow}); db.SaveChanges();
                var required=Signers(c); var accepted=db.ContractAcceptances.Where(a=>a.ContractVersionId==v.Id && a.AcceptedHash==hash).Select(a=>a.UserAccountId).ToArray();
                if(required.All(accepted.Contains))
                {
                    c.Status="AwaitingEscrow";
                    NotificationService.Queue(db,c.Project.ClientId,"Hợp đồng #"+id+" đủ xác nhận; có thể bảo đảm mốc đầu","/Contracts/Details/"+id);
                }
                db.SaveChanges();tx.Commit();
            }
        }
        public void Revise(int id,int clientId,string scope,string milestonePlan,int deliveryDays)
        {
            Must(!string.IsNullOrWhiteSpace(scope) && scope.Trim().Length>=20 && scope.Length<=8000 && deliveryDays>=1 && deliveryDays<=3650,"Phạm vi (20–8000 ký tự) hoặc thời hạn không hợp lệ.");
            using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var c=Lock(id); Must(c.Project.ClientId==clientId,"Chỉ Client được đề nghị phiên bản điều khoản mới.");
                Must(c.Status=="AwaitingSignatures" || c.Status=="AwaitingEscrow","Chỉ tạo phiên bản mới trước khi có bảo đảm.");
                Must(!db.PaymentIntents.Any(p=>p.ContractId==id),"Đã có yêu cầu thanh toán; cần đối soát trước khi thay điều khoản.");
                var old=db.EscrowMilestones.Where(m=>m.ContractId==id).ToList(); Must(old.All(m=>m.Status=="Planned"),"Không thay điều khoản của mốc đã bảo đảm.");
                var snapshot=ReadSnapshot(Current(c)); var plan=PaymentPlan.Parse(milestonePlan,snapshot.Price,c.ProjectId,deliveryDays);
                snapshot.Scope=scope.Trim();snapshot.DeliveryDays=deliveryDays;snapshot.Milestones=plan.Select(m=>new ContractPlanItem {Title=m.Title,Amount=m.Amount,DueDay=m.DueDay,AcceptanceCriteria=m.AcceptanceCriteria}).ToList();
                c.CurrentVersion++;c.Status="AwaitingSignatures";AddVersion(c,snapshot);
                db.EscrowMilestones.RemoveRange(old);
                foreach(var m in plan) {m.ContractId=id;db.EscrowMilestones.Add(m);}
                foreach(var user in Signers(c)) NotificationService.Queue(db,user,"Điều khoản hợp đồng #"+id+" có phiên bản mới, cần xác nhận lại","/Contracts/Details/"+id);
                db.SaveChanges();tx.Commit();
            }
        }
        public void RequireFundingReady(int id)
        {
            var c=db.Contracts.Include(x=>x.Project).Single(x=>x.Id==id);
            Must(c.Status=="AwaitingEscrow" || c.Status=="Active","Hợp đồng chưa đủ xác nhận hoặc đang tạm dừng.");
            var v=Current(c);var snapshot=ReadSnapshot(v);var required=Signers(c);
            var accepted=db.ContractAcceptances.Where(a=>a.ContractVersionId==v.Id && a.AcceptedHash==v.Hash).Select(a=>a.UserAccountId).ToArray();
            Must(required.All(accepted.Contains),"Chưa đủ xác nhận đúng phiên bản điều khoản.");
            var members=db.ContractMembers.Where(m=>m.ContractId==id).ToList();
            var shares=db.EscrowShares.Where(m=>m.ContractId==id).ToList();
            Must(members.Count==snapshot.Members.Count && shares.Count==members.Count && snapshot.Members.All(item=>members.Any(m=>m.UserAccountId==item.UserId && m.ProposalId==item.ProposalId && m.Weight==item.Weight) && shares.Any(s=>s.UserAccountId==item.UserId && s.Weight==item.Weight)),"Đội hình hoặc phân chia không khớp điều khoản đã xác nhận.");
            var plan=db.EscrowMilestones.Where(m=>m.ContractId==id).OrderBy(m=>m.Id).ToList();
            Must(snapshot.Milestones.Count==plan.Count && plan.Select((m,i)=>m.Amount==snapshot.Milestones[i].Amount && m.Title==snapshot.Milestones[i].Title && m.DueDay==snapshot.Milestones[i].DueDay && m.AcceptanceCriteria==snapshot.Milestones[i].AcceptanceCriteria).All(x=>x),"Kế hoạch mốc không khớp phiên bản đã xác nhận.");
        }
        public void Decline(int id,int userId,string reason)
        {
            Must(!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length>=10 && reason.Length<=500,"Nêu lý do từ 10 đến 500 ký tự.");
            using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var c=Lock(id);Must(CanView(c,userId),"Không có quyền từ chối hợp đồng.");
                Must((c.Status=="AwaitingSignatures" || c.Status=="AwaitingEscrow") && !db.PaymentIntents.Any(p=>p.ContractId==id),"Chỉ từ chối trước thanh toán; sau đó cần quy trình quyết toán.");
                Must(!db.EscrowMilestones.Any(m=>m.ContractId==id && m.Status!="Planned"),"Đã có khoản bảo đảm.");
                c.Status="Declined";
                var proposalIds=db.ContractMembers.Where(m=>m.ContractId==id).Select(m=>m.ProposalId).ToArray();
                var assignments=db.RoleAssignments.Where(a=>proposalIds.Contains(a.ProposalId)).ToList();var assignmentIds=assignments.Select(a=>a.Id).ToArray();
                db.TeamMembers.RemoveRange(db.TeamMembers.Where(m=>assignmentIds.Contains(m.RoleAssignmentId)));db.RoleAssignments.RemoveRange(assignments);
                // A vacancy must be recruitable even if hiring was closed before signing.
                var team=db.ProjectTeams.Single(t=>t.ProjectId==c.ProjectId);team.Locked=false;
                if(c.Project.Status!="Paused")c.Project.Status="Published";
                foreach(var p in db.Proposals.Where(p=>proposalIds.Contains(p.Id))) p.Status="Rejected";
                if(c.GroupBidId.HasValue) db.GroupBids.Single(b=>b.Id==c.GroupBidId).Status="Rejected";
                db.ActivityLogs.Add(new ActivityLog {ProjectId=c.ProjectId,ActorId=userId,Action="Declined contract #"+id+": "+reason.Trim().Substring(0,Math.Min(reason.Trim().Length,150)),CreatedAtUtc=DateTime.UtcNow});
                foreach (var recipient in Signers(c).Where(s => s != userId))
                    NotificationService.Queue(db, recipient, "Hợp đồng #" + id + " đã bị từ chối: " + reason.Trim().Substring(0, Math.Min(130, reason.Trim().Length)), "/Contracts/Details/" + id, userId, c.ProjectId);
                db.SaveChanges();tx.Commit();
            }
        }
    }
}
