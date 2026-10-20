using System;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public class GroupsController : BaseController
    {
        private FreelancerGroupService Groups => new FreelancerGroupService(Db);

        [HttpGet]
        public ActionResult Index()
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            return View(Groups.LoadIndex(CurrentUser.Id));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Create(CreateGroupForm form)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            if (form != null && !IndustryCatalog.IsValid(form.Industry)) ModelState.AddModelError("Industry", "Ngành nghề không hợp lệ.");
            if (form == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(form.Name) || string.IsNullOrWhiteSpace(form.Description))
            {
                TempData["Error"] = "Vui lòng nhập tên và mô tả nhóm hợp lệ.";
                return RedirectToAction("Index");
            }
            int id = Groups.Create(CurrentUser.Id, form);
            TempData["Notice"] = "Đã tạo nhóm Freelancer.";
            return RedirectToAction("Details", new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Update(int id, CreateGroupForm form)
        {
            if(CurrentUser?.Role != "Freelancer") return Forbidden();
            var group = Db.FreelancerGroups.SingleOrDefault(g => g.Id == id);
            if(group == null) return HttpNotFound();
            if(group.LeaderId != CurrentUser.Id) return Forbidden();
            if(form == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(form.Name) || string.IsNullOrWhiteSpace(form.Description) || !IndustryCatalog.IsValid(form.Industry)) { TempData["Error"]="Kiểm tra tên, mô tả, quy mô, hình thức làm việc và ngân sách của nhóm."; return RedirectToAction("Details", new { id }); }
            group.Name=form.Name.Trim(); group.Description=form.Description.Trim(); group.Industry=IndustryCatalog.Normalize(form.Industry);
            group.WorkStyle=form.WorkStyle ?? "Remote"; group.Specialties=form.Specialties?.Trim(); group.Collaboration=form.Collaboration?.Trim(); group.TargetSize=form.TargetSize; group.MinimumBudget=form.MinimumBudget;
            Db.FreelancerGroupEvents.Add(new NexusFreelance.Web.Models.FreelancerGroupEvent { FreelancerGroupId=id, ActorId=CurrentUser.Id, Description="Cập nhật hồ sơ chuyên môn và cách phối hợp của nhóm", CreatedAtUtc=DateTime.UtcNow });
            foreach(var member in Db.FreelancerGroupMembers.Where(m => m.FreelancerGroupId == id && m.Status == "Active" && m.UserAccountId != CurrentUser.Id).Select(m => m.UserAccountId).ToList()) NotificationService.Queue(Db,member,"Thông tin nhóm " + group.Name + " vừa được cập nhật", "/Groups/Details/"+id, CurrentUser.Id);
            Db.SaveChanges(); TempData["Notice"]="Đã cập nhật hồ sơ nhóm.";
            return RedirectToAction("Details", new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult UpdateVisibility(int id, bool isPublic)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            try
            {
                Groups.UpdateVisibility(id, CurrentUser.Id, isPublic);
                TempData["Notice"] = isPublic ? "Nhóm hiện trên danh bạ công khai." : "Đã ẩn nhóm khỏi danh bạ công khai.";
            }
            catch (UnauthorizedAccessException) { return Forbidden(); }
            catch (InvalidOperationException) { return HttpNotFound(); }
            return RedirectToAction("Details", new { id });
        }

        [HttpGet]
        public ActionResult Details(int id)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            var page = Groups.LoadDetails(id, CurrentUser.Id);
            if (page != null)
            {
                ViewBag.GroupBids = Db.GroupBids.Include("Project")
                    .Where(b => b.FreelancerGroupId == id).OrderByDescending(b => b.CreatedAtUtc).ToList();
                ViewBag.GroupDashboard = Groups.LoadDashboard(id, CurrentUser.Id);
                if (page.IsLeader) {
                    var today = DateTime.UtcNow.Date;
                    ViewBag.StaffingProjects = Db.Projects.Where(p => p.Status == "Published" && p.ProjectType != "SOLO" && (!p.DeadlineUtc.HasValue || p.DeadlineUtc >= today)).OrderByDescending(p => p.Id).Take(30).ToList();
                    ViewBag.StaffingPlans = Db.GroupProjectRecruitments.Include("Project").Where(r => r.FreelancerGroupId == id).OrderByDescending(r => r.Id).Take(20).ToList();
                }
                var members = page.Members.Where(m => m.Status == "Active").Select(m => m.UserAccountId).ToArray();
                ViewBag.GroupSkills = new SmartMatchingService(Db).Evidence(members).Values.SelectMany(v => v).GroupBy(s => s.SkillId).Select(g => g.OrderByDescending(s => s.Level).First()).ToList();
            }
            return page == null ? (ActionResult)HttpNotFound() : View(page);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Invite(GroupInviteForm form)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            if (form == null || form.GroupId <= 0 || !ModelState.IsValid)
                return new HttpStatusCodeResult(400);
            try
            {
                Groups.Invite(form.GroupId, CurrentUser.Id, form.Email.Trim().ToLowerInvariant());
                TempData["Notice"] = "Đã gửi lời mời vào nhóm.";
            }
            catch (UnauthorizedAccessException) { return Forbidden(); }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            catch (System.Data.Entity.Infrastructure.DbUpdateException) { TempData["Error"] = "Thành viên vừa được cập nhật. Vui lòng thử lại."; }
            catch (System.Data.SqlClient.SqlException) { TempData["Error"] = "Không thể cập nhật lời mời lúc này."; }
            return RedirectToAction("Details", new { id = form.GroupId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Respond(int id, bool accept)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            try
            {
                Groups.Respond(id, CurrentUser.Id, accept);
                TempData["Notice"] = accept ? "Bạn đã tham gia nhóm." : "Bạn đã từ chối lời mời.";
            }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            catch (System.Data.SqlClient.SqlException) { TempData["Error"] = "Lời mời vừa được xử lý. Hãy tải lại trang."; }
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Remove(int id, int memberId)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            try { Groups.ChangeMembership(id, CurrentUser.Id, memberId, false); TempData["Notice"] = "Đã cập nhật thành viên."; }
            catch (UnauthorizedAccessException) { return Forbidden(); }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            catch (System.Data.SqlClient.SqlException) { TempData["Error"] = "Không thể cập nhật thành viên lúc này."; }
            return RedirectToAction("Details", new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Leave(int id)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            try { Groups.ChangeMembership(id, CurrentUser.Id, CurrentUser.Id, true); TempData["Notice"] = "Bạn đã rời nhóm."; }
            catch (UnauthorizedAccessException) { return Forbidden(); }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            catch (System.Data.SqlClient.SqlException) { TempData["Error"] = "Không thể rời nhóm lúc này."; }
            return RedirectToAction("Index");
        }
    }
}
