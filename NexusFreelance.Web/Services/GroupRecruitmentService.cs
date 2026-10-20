using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;
namespace NexusFreelance.Web.Services
{
 public sealed class GroupRecruitmentService
 {
  private readonly AppDbContext db;
  private readonly Dictionary<int,List<PositionRecommendation>> positionCache=new Dictionary<int,List<PositionRecommendation>>();
  public GroupRecruitmentService(AppDbContext context){db=context;}
  private static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
  private Project LoadProject(int id)=>db.Projects.AsNoTracking().Include(p=>p.Client).Include(p=>p.Requirements).SingleOrDefault(p=>p.Id==id);
  private FreelancerGroup LoadGroup(int id)=>db.FreelancerGroups.AsNoTracking().Include(g=>g.Leader).SingleOrDefault(g=>g.Id==id);
  private void Lock(int groupId,int projectId) {
   Need(db.Database.SqlQuery<int>("SELECT Id FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0",projectId).SingleOrDefault()>0,"Dự án không tồn tại.");
   Need(db.Database.SqlQuery<int>("SELECT Id FROM dbo.FreelancerGroups WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0",groupId).SingleOrDefault()>0,"Nhóm không tồn tại.");
   positionCache.Remove(projectId);
  }
  public GroupStaffingPage Build(int groupId,int projectId,bool includePrivateRequests=false) {
   var p=LoadProject(projectId);var g=LoadGroup(groupId);Need(p!=null && g!=null,"Nhóm hoặc dự án không tồn tại.");
   var page=new GroupStaffingPage {Group=g,Project=p,Recruitment=db.GroupProjectRecruitments.SingleOrDefault(r=>r.FreelancerGroupId==groupId && r.ProjectId==projectId)};
   if(page.Recruitment!=null)db.Entry(page.Recruitment).Reload();
   var memberRows=db.FreelancerGroupMembers.AsNoTracking().Where(m=>m.FreelancerGroupId==groupId && (m.Status=="Active" || m.Status=="Invited")).Select(m=>new{m.UserAccountId,m.Status}).ToList();
   var active=memberRows.Where(m=>m.Status=="Active").Select(m=>m.UserAccountId).ToArray();page.MemberCount=active.Length;
   var contracted=db.RoleAssignments.Where(a=>a.ProjectId==projectId).Select(a=>a.FreelancerId).ToArray();
   var allMemberIds=memberRows.Select(m=>m.UserAccountId).ToArray();
   var visibleMembers=db.FreelancerProfiles.AsNoTracking().Where(f=>allMemberIds.Contains(f.UserAccountId) && !contracted.Contains(f.UserAccountId) && f.IsPublic!=false && f.IsAvailable && !f.User.IsSuspended && f.User.EmailVerifiedAtUtc.HasValue).Select(f=>f.UserAccountId).ToArray();
   var evidence=new SmartMatchingService(db).Evidence(visibleMembers);
   List<PositionRecommendation> original;
   if(!positionCache.TryGetValue(projectId,out original)){original=new PositionMatchingService(db).ForProject(p,500);positionCache[projectId]=original;}
   page.Positions=original.Select(r=>new PositionRecommendation {RoleId=r.RoleId,Title=r.Title,RequiredCount=r.Remaining,Remaining=r.Remaining,Skills=r.Skills,People=r.People.ToList()}).ToList();
   var coverage=PositionMatchingService.AssignSlots(page.Positions,evidence,active);
   var bids=db.Proposals.Where(b=>b.ProjectId==projectId).Select(b=>new{b.ApplicantId,b.RoleRequirementId}).ToList();
   foreach(var role in page.Positions) {
    role.FilledCount=coverage.Count(c=>c.Value==role.RoleId && active.Contains(c.Key));role.AwaitingCount=coverage.Count(c=>c.Value==role.RoleId && !active.Contains(c.Key));role.Remaining=Math.Max(0,role.RequiredCount-role.FilledCount-role.AwaitingCount);
    role.People=role.Remaining==0?new List<MatchCard>():role.People.Where(c=>c.Required>0 && c.Covered==c.Required && !memberRows.Any(m=>m.UserAccountId==c.Id) && !bids.Any(b=>b.ApplicantId==c.Id && b.RoleRequirementId==role.RoleId)).Take(6).ToList();
   }
   var publicLeader=db.FreelancerProfiles.Any(f=>f.UserAccountId==g.LeaderId && f.IsPublic!=false);
   var accepting=p.Status=="Published" && RecruitmentPolicy.DateAllowsApplications(p.DeadlineUtc,DateTime.UtcNow) && p.ProjectType!="SOLO";
   var room=memberRows.Count<(g.TargetSize??50);
   page.CanRecruit=accepting && g.IsPublic!=false && !g.Leader.IsSuspended && g.Leader.Role=="Freelancer" && g.Leader.EmailVerifiedAtUtc.HasValue && g.Leader.Email!=p.Client.Email && (p.Client.GoogleSubject==null || g.Leader.GoogleSubject!=p.Client.GoogleSubject) && publicLeader && room && page.Positions.Any(r=>r.Remaining>0 && r.Skills.Count>0);
   page.IsOpen=page.CanRecruit && page.Recruitment?.Status=="Open";
   page.AvailabilityNote=!accepting?"Dự án đã dừng hoặc hết hạn tuyển; chỉ xem lại kế hoạch nhân sự.":!room?"Quy mô nhóm đã đủ hoặc đang chờ xác nhận lời mời. Kiểm tra đội hình trước khi mời thêm.":!page.Positions.Any(r=>r.Remaining>0)?(page.Positions.Any(r=>r.AwaitingCount>0)?"Các vị trí còn chờ xác nhận lời mời. Chỉ lập đề xuất sau khi thành viên đã đồng ý.":"Đội hình hiện tại đã bao phủ các vị trí còn tuyển. Có thể chuyển sang lập đề xuất nhóm."):"Tính theo kỹ năng còn hiệu lực của thành viên công khai đang nhận việc; mỗi người chỉ tính cho một vị trí.";
   if(includePrivateRequests)page.PendingInvitations=db.FreelancerGroupMembers.AsNoTracking().Where(m=>m.FreelancerGroupId==groupId && m.Status=="Invited").Select(m=>new PendingStaffInvitation{UserId=m.UserAccountId,Name=m.User.Name}).ToList();
   if(includePrivateRequests && page.Recruitment!=null)page.Requests=db.GroupJoinRequests.Include(r=>r.Freelancer).Include(r=>r.Role).Where(r=>r.GroupProjectRecruitmentId==page.Recruitment.Id).OrderByDescending(r=>r.CreatedAtUtc).Take(100).ToList();
   return page;
  }
  public int Open(int groupId,int projectId,int leaderId) {
   using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted)) {
    Lock(groupId,projectId);var page=Build(groupId,projectId);if(page.Group.LeaderId!=leaderId)throw new UnauthorizedAccessException();
    Need(db.FreelancerGroupMembers.Any(m=>m.FreelancerGroupId==groupId && m.UserAccountId==leaderId && m.Status=="Active"),"Đại diện chưa là thành viên hoạt động.");
    Need(page.CanRecruit,page.AvailabilityNote);var item=page.Recruitment;
    if(item==null){item=new GroupProjectRecruitment {FreelancerGroupId=groupId,ProjectId=projectId,Status="Open",CreatedAtUtc=DateTime.UtcNow};db.GroupProjectRecruitments.Add(item);}else{item.Status="Open";item.ClosedAtUtc=null;}
    db.SaveChanges();tx.Commit();return item.Id;
   }
  }
  public void Close(int id,int leaderId) {
   var item=db.GroupProjectRecruitments.SingleOrDefault(r=>r.Id==id);Need(item!=null,"Nhu cầu không tồn tại.");
   using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted)){Lock(item.FreelancerGroupId,item.ProjectId);if(LoadGroup(item.FreelancerGroupId).LeaderId!=leaderId)throw new UnauthorizedAccessException();item.Status="Closed";item.ClosedAtUtc=DateTime.UtcNow;foreach(var request in db.GroupJoinRequests.Where(r=>r.GroupProjectRecruitmentId==id && r.Status=="Pending")){request.Status="Rejected";request.DecidedAtUtc=DateTime.UtcNow;NotificationService.Queue(db,request.FreelancerId,"Nhóm đã đóng nhu cầu bổ sung cho dự án","/Groups",leaderId,item.ProjectId);}db.SaveChanges();tx.Commit();}
  }
  private MatchCard EligibleCandidate(GroupStaffingPage page,int roleId,int userId) {
   Need(page.IsOpen,"Nhu cầu bổ sung hiện không nhận đề nghị mới.");
   var role=page.Positions.SingleOrDefault(r=>r.RoleId==roleId && r.Remaining>0);Need(role!=null,"Vị trí đã đủ hoặc không thuộc nhu cầu nhóm.");
   Need(!db.FreelancerGroupMembers.Any(m=>m.FreelancerGroupId==page.Group.Id && m.UserAccountId==userId && (m.Status=="Active" || m.Status=="Invited")),"Bạn đã tham gia hoặc đang có lời mời vào nhóm.");
   Need(!db.RoleAssignments.Any(a=>a.ProjectId==page.Project.Id && a.FreelancerId==userId) && !db.Proposals.Any(p=>p.ProjectId==page.Project.Id && p.RoleRequirementId==roleId && p.ApplicantId==userId),"Ứng viên đã có liên hệ tuyển dụng với vị trí này; cần kiểm tra trước khi đưa vào đội hình mới.");
   var matching=new PositionMatchingService(db);var person=matching.Eligible(page.Project).SingleOrDefault(p=>p.UserAccountId==userId);Need(person!=null,"Hồ sơ chưa đủ điều kiện công khai hoặc nhận việc.");
   var matcher=new SmartMatchingService(db);var evidence=matcher.Evidence(new[]{userId});var selected=matcher.SelectedRoles(new List<FreelancerProfile>{person},evidence);
   var card=PositionMatchingService.Score(new RoleRequirement {Title=role.Title},page.Project,role.Skills,person,evidence[userId],selected[userId]);Need(card.Required>0 && card.Covered==card.Required,"Minh chứng hiện tại chưa đáp ứng mức cần bổ sung cho vị trí.");return card;
  }
  public void Request(int recruitmentId,int userId,int roleId,string pitch) {
   Need(!string.IsNullOrWhiteSpace(pitch) && pitch.Trim().Length>=20 && pitch.Length<=600,"Giới thiệu năng lực liên quan từ 20 đến 600 ký tự.");
   var item=db.GroupProjectRecruitments.SingleOrDefault(r=>r.Id==recruitmentId);Need(item!=null,"Nhu cầu không tồn tại.");
   using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted)) {
    Lock(item.FreelancerGroupId,item.ProjectId);var page=Build(item.FreelancerGroupId,item.ProjectId);EligibleCandidate(page,roleId,userId);
    Need(!db.GroupJoinRequests.Any(r=>r.GroupProjectRecruitmentId==recruitmentId && r.FreelancerId==userId),"Bạn đã gửi đề nghị cho nhu cầu này. Xem trạng thái hoặc chờ trưởng nhóm phản hồi.");
    db.GroupJoinRequests.Add(new GroupJoinRequest {GroupProjectRecruitmentId=recruitmentId,FreelancerId=userId,RoleRequirementId=roleId,Pitch=pitch.Trim(),Status="Pending",CreatedAtUtc=DateTime.UtcNow});
    NotificationService.Queue(db,page.Group.LeaderId,"Đề nghị bổ sung vị trí: "+page.Positions.Single(r=>r.RoleId==roleId).Title,"/GroupRecruitment/Manage?groupId="+page.Group.Id+"&projectId="+page.Project.Id,userId,page.Project.Id);db.SaveChanges();tx.Commit();
   }
  }
  public void Invite(int recruitmentId,int leaderId,int userId,int roleId) {
   var item=db.GroupProjectRecruitments.SingleOrDefault(r=>r.Id==recruitmentId);Need(item!=null,"Nhu cầu không tồn tại.");
   using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted)) {
    Lock(item.FreelancerGroupId,item.ProjectId);var page=Build(item.FreelancerGroupId,item.ProjectId);if(page.Group.LeaderId!=leaderId)throw new UnauthorizedAccessException();EligibleCandidate(page,roleId,userId);
    new FreelancerGroupService(db).InviteUnderLock(page.Group.Id,leaderId,db.Users.Where(u=>u.Id==userId).Select(u=>u.Email).Single());
    var request=db.GroupJoinRequests.SingleOrDefault(r=>r.GroupProjectRecruitmentId==recruitmentId && r.FreelancerId==userId && r.Status=="Pending");if(request!=null){request.Status="Invited";request.DecidedAtUtc=DateTime.UtcNow;}db.SaveChanges();tx.Commit();
   }
  }
  public void Decide(int requestId,int leaderId,bool approve) {
   var request=db.GroupJoinRequests.Include(r=>r.Recruitment).SingleOrDefault(r=>r.Id==requestId);Need(request!=null,"Đề nghị không tồn tại.");
   using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted)) {
    Lock(request.Recruitment.FreelancerGroupId,request.Recruitment.ProjectId);
    var page=Build(request.Recruitment.FreelancerGroupId,request.Recruitment.ProjectId);
    if(page.Group.LeaderId!=leaderId)throw new UnauthorizedAccessException();db.Entry(request).Reload();Need(request.Status=="Pending","Đề nghị đã được xử lý.");
    if(approve){EligibleCandidate(page,request.RoleRequirementId,request.FreelancerId);new FreelancerGroupService(db).InviteUnderLock(page.Group.Id,leaderId,db.Users.Where(u=>u.Id==request.FreelancerId).Select(u=>u.Email).Single());request.Status="Invited";}
    else{request.Status="Rejected";NotificationService.Queue(db,request.FreelancerId,"Nhóm đã phản hồi đề nghị tham gia; kiểm tra nhu cầu khác phù hợp","/Groups",leaderId,request.Recruitment.ProjectId);}
    request.DecidedAtUtc=DateTime.UtcNow;db.SaveChanges();tx.Commit();
   }
  }
  public void CancelInvitation(int recruitmentId,int leaderId,int userId) {
   var plan=db.GroupProjectRecruitments.SingleOrDefault(r=>r.Id==recruitmentId);Need(plan!=null,"Nhu cầu không tồn tại.");
   using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted)){
    Lock(plan.FreelancerGroupId,plan.ProjectId);if(LoadGroup(plan.FreelancerGroupId).LeaderId!=leaderId)throw new UnauthorizedAccessException();
    var member=db.FreelancerGroupMembers.SingleOrDefault(m=>m.FreelancerGroupId==plan.FreelancerGroupId && m.UserAccountId==userId);Need(member!=null && member.Status=="Invited","Chỉ hủy lời mời còn chờ xác nhận.");
    member.Status="Declined";member.RespondedAtUtc=DateTime.UtcNow;var request=db.GroupJoinRequests.SingleOrDefault(r=>r.GroupProjectRecruitmentId==recruitmentId && r.FreelancerId==userId && r.Status=="Invited");if(request!=null){request.Status="Rejected";request.DecidedAtUtc=DateTime.UtcNow;}
    db.FreelancerGroupEvents.Add(new FreelancerGroupEvent {FreelancerGroupId=plan.FreelancerGroupId,ActorId=leaderId,Description="Hủy lời mời bổ sung đang chờ xác nhận",CreatedAtUtc=DateTime.UtcNow});NotificationService.Queue(db,userId,"Trưởng nhóm đã hủy lời mời bổ sung đang chờ","/Groups",leaderId,plan.ProjectId);db.SaveChanges();tx.Commit();
   }
  }
  public List<GroupOpportunity> Opportunities(Project project,int userId) {
   if(project.ProjectType=="SOLO" || project.Status!="Published" || !RecruitmentPolicy.DateAllowsApplications(project.DeadlineUtc,DateTime.UtcNow))return new List<GroupOpportunity>();
   var open=db.GroupProjectRecruitments.AsNoTracking().Where(r=>r.ProjectId==project.Id && r.Status=="Open" && r.Group.LeaderId!=userId && r.Group.IsPublic!=false && !r.Group.Leader.IsSuspended).OrderByDescending(r=>r.Id).Take(20).ToList();var result=new List<GroupOpportunity>();
   foreach(var item in open) {
    var page=Build(item.FreelancerGroupId,item.ProjectId);if(!page.IsOpen)continue;
    var existing=db.GroupJoinRequests.AsNoTracking().SingleOrDefault(r=>r.GroupProjectRecruitmentId==item.Id && r.FreelancerId==userId);
    foreach(var role in page.Positions.Where(p=>p.Remaining>0 && p.Skills.Count>0)) {
     try{var card=EligibleCandidate(page,role.RoleId,userId);result.Add(new GroupOpportunity {RecruitmentId=item.Id,GroupId=page.Group.Id,GroupName=page.Group.Name,LeaderName=page.Group.Leader.Name,RoleId=role.RoleId,RoleTitle=role.Title,Missing=role.Remaining,Score=card.Score,Reasons=card.Reasons,RequestStatus=existing?.Status});break;}catch(InvalidOperationException){ }
    }
   }
   return result.OrderByDescending(r=>r.Score).ThenByDescending(r=>r.RecruitmentId).Take(8).ToList();
  }
 }
}
