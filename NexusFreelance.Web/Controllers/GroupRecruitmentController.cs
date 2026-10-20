using System;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Services;
namespace NexusFreelance.Web.Controllers
{
 [Authorize]
 public class GroupRecruitmentController:BaseController
 {
  private GroupRecruitmentService Staffing=>new GroupRecruitmentService(Db);
  [HttpGet] public ActionResult Manage(int groupId,int projectId) {
   if(CurrentUser.Role!="Freelancer")return Forbidden();var group=Db.FreelancerGroups.SingleOrDefault(g=>g.Id==groupId);if(group==null)return HttpNotFound();if(group.LeaderId!=CurrentUser.Id)return Forbidden();
   var project=Db.Projects.SingleOrDefault(p=>p.Id==projectId);if(project==null || project.Status=="Draft")return HttpNotFound();
   if((project.Status=="Paused" || project.Status=="Cancelled") && !Db.GroupProjectRecruitments.Any(r=>r.FreelancerGroupId==groupId && r.ProjectId==projectId))return HttpNotFound();
   var page=Staffing.Build(groupId,projectId,true);page.IsLeader=true;return View(page);
  }
  private ActionResult Execute(Action operation,int groupId,int projectId,bool returnToProject=false) {
   if(CurrentUser.Role!="Freelancer")return Forbidden();if(!ModelState.IsValid)return new HttpStatusCodeResult(400);try{operation();TempData["Notice"]="Đã cập nhật nhu cầu hoặc lời mời. Thành viên chỉ tham gia sau khi tự xác nhận lời mời.";}catch(UnauthorizedAccessException){return Forbidden();}catch(InvalidOperationException ex){TempData["Error"]=ex.Message;}catch(System.Data.Entity.Infrastructure.DbUpdateException){TempData["Error"]="Dữ liệu đã cập nhật đồng thời. Kiểm tra trạng thái trước khi gửi lại.";}catch(System.Data.SqlClient.SqlException){TempData["Error"]="Nhu cầu vừa thay đổi. Hãy tải lại để kiểm tra.";}
   return returnToProject?RedirectToAction("Details","Projects",new{id=projectId}):RedirectToAction("Manage",new{groupId,projectId});
  }
  [HttpPost,ValidateAntiForgeryToken] public ActionResult Open(int groupId,int projectId)=>Execute(()=>Staffing.Open(groupId,projectId,CurrentUser.Id),groupId,projectId);
  [HttpPost,ValidateAntiForgeryToken] public ActionResult Close(int id) {
   var item=Db.GroupProjectRecruitments.SingleOrDefault(r=>r.Id==id);if(item==null)return HttpNotFound();return Execute(()=>Staffing.Close(id,CurrentUser.Id),item.FreelancerGroupId,item.ProjectId);
  }
  [HttpPost,ValidateAntiForgeryToken] public ActionResult ApplyToGroup(int id,int roleId,string pitch) {
   var item=Db.GroupProjectRecruitments.SingleOrDefault(r=>r.Id==id);if(item==null)return HttpNotFound();return Execute(()=>Staffing.Request(id,CurrentUser.Id,roleId,pitch),item.FreelancerGroupId,item.ProjectId,true);
  }
  [HttpPost,ValidateAntiForgeryToken] public ActionResult Invite(int id,int userId,int roleId) {
   var item=Db.GroupProjectRecruitments.SingleOrDefault(r=>r.Id==id);if(item==null)return HttpNotFound();return Execute(()=>Staffing.Invite(id,CurrentUser.Id,userId,roleId),item.FreelancerGroupId,item.ProjectId);
  }
  [HttpPost,ValidateAntiForgeryToken] public ActionResult CancelInvitation(int id,int userId) {
   var item=Db.GroupProjectRecruitments.SingleOrDefault(r=>r.Id==id);if(item==null)return HttpNotFound();return Execute(()=>Staffing.CancelInvitation(id,CurrentUser.Id,userId),item.FreelancerGroupId,item.ProjectId);
  }
  [HttpPost,ValidateAntiForgeryToken] public ActionResult Decide(int id,bool approve) {
   var item=Db.GroupJoinRequests.Where(r=>r.Id==id).Select(r=>r.Recruitment).SingleOrDefault();if(item==null)return HttpNotFound();return Execute(()=>Staffing.Decide(id,CurrentUser.Id,approve),item.FreelancerGroupId,item.ProjectId);
  }
 }
}
