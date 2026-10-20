using System;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public class GroupBidsController : BaseController
    {
        private GroupBidService Bids => new GroupBidService(Db);

        [HttpGet]
        public ActionResult Create(int groupId, int projectId)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            var page = Bids.CreatePage(groupId, projectId, CurrentUser.Id);
            return page == null ? (ActionResult)HttpNotFound() : View(page);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Create(GroupBidForm form)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            if (form == null || !ModelState.IsValid || form.ProjectId <= 0 || form.FreelancerGroupId <= 0)
                return new HttpStatusCodeResult(400, "Dữ liệu Proposal nhóm không hợp lệ.");
            try
            {
                Bids.Submit(CurrentUser.Id, form);
                TempData["Notice"] = "Đã gửi Proposal nhóm; mỗi vị trí có một Matching snapshot riêng.";
                return RedirectToAction("Details", "Groups", new { id = form.FreelancerGroupId });
            }
            catch (InvalidOperationException ex) { return new HttpStatusCodeResult(409, ex.Message); }
            catch (System.Data.Entity.Infrastructure.DbUpdateException) { return new HttpStatusCodeResult(409, "Đội hình hoặc Proposal đã được cập nhật."); }
            catch (System.Data.SqlClient.SqlException) { return new HttpStatusCodeResult(409, "Không thể lưu Proposal nhóm lúc này."); }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Decide(int id, bool accept)
        {
            if (CurrentUser?.Role != "Client") return Forbidden();
            var bid = Db.GroupBids.SingleOrDefault(b => b.Id == id);
            if (bid == null) return HttpNotFound();
            if (Db.Projects.Where(p => p.Id == bid.ProjectId).Select(p => p.ClientId).Single() != CurrentUser.Id)
                return Forbidden();
            try
            {
                Bids.Decide(id, CurrentUser.Id, accept);
                TempData["Notice"] = accept ? "Đã chấp nhận toàn bộ đội hình nhóm." : "Đã từ chối Proposal nhóm.";
                return RedirectToAction("Manage", "Projects", new { id = bid.ProjectId });
            }
            catch (InvalidOperationException ex) { return new HttpStatusCodeResult(409, ex.Message); }
            catch (System.Data.Entity.Infrastructure.DbUpdateException) { return new HttpStatusCodeResult(409, "Đội hình vừa thay đổi."); }
            catch (System.Data.SqlClient.SqlException) { return new HttpStatusCodeResult(409, "Vị trí tuyển vừa thay đổi. Thử lại."); }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Withdraw(int id)
        {
            if (CurrentUser?.Role != "Freelancer") return Forbidden();
            var bid = Db.GroupBids.SingleOrDefault(b => b.Id == id);
            if (bid == null) return HttpNotFound();
            if (bid.LeaderId != CurrentUser.Id) return Forbidden();
            try
            {
                Bids.Withdraw(id, CurrentUser.Id);
                TempData["Notice"] = "Đã rút Proposal nhóm; lịch sử được giữ lại.";
                return RedirectToAction("Details", "Groups", new { id = bid.FreelancerGroupId });
            }
            catch (InvalidOperationException ex) { return new HttpStatusCodeResult(409, ex.Message); }
            catch (System.Data.SqlClient.SqlException) { return new HttpStatusCodeResult(409, "Không thể rút Proposal nhóm lúc này."); }
        }
    }
}
