using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Services;
namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public class SkillEvidenceController : BaseController
    {
        private SkillEvidenceService Evidence => new SkillEvidenceService(Db);
        [HttpGet]
        public ActionResult Index()
        {
            if(CurrentUser.Role!="Freelancer")return Forbidden();
            var today=DateTime.Today;ViewBag.VerifiedIds=Db.SkillEvidenceRequests.Where(r=>r.Claim.UserAccountId==CurrentUser.Id && r.Status=="Approved" && (!r.ValidUntil.HasValue || r.ValidUntil>=today)).Select(r=>r.SkillClaimId).Distinct().ToList();
            ViewBag.Claims=Db.SkillClaims.Include(c=>c.Skill).Where(c=>c.UserAccountId==CurrentUser.Id && !c.IsDeleted).OrderBy(c=>c.Skill.Title).ToList();
            ViewBag.Tracks=Db.SkillTracks.Where(s=>s.IsActive!=false).OrderBy(s=>s.Title).ToList();
            ViewBag.Requests=Db.SkillEvidenceRequests.Include(r=>r.Claim.Skill).Where(r=>r.Claim.UserAccountId==CurrentUser.Id).OrderByDescending(r=>r.Id).Take(50).ToList();
            ViewBag.Attempts=Db.SkillExamAttempts.Include(a=>a.SkillTrack).Where(a=>a.UserAccountId==CurrentUser.Id && a.Passed &&
                !Db.SkillExamAttempts.Any(b=>b.UserAccountId==a.UserAccountId && b.SkillTrackId==a.SkillTrackId && b.Passed &&
                    (b.Percent>a.Percent || (b.Percent==a.Percent && b.Id>a.Id)))).OrderBy(a=>a.SkillTrack.Title).ToList();
            ViewBag.Portfolio=Db.PortfolioItems.Where(p=>p.FreelancerId==CurrentUser.Id).OrderByDescending(p=>p.Id).ToList();
            return View();
        }
        [HttpGet]
        public ActionResult Admin()
        {
            if(CurrentUser.Role!="Admin")return Forbidden();
            var requests=Db.SkillEvidenceRequests.Include(r=>r.Claim.Skill).Include(r=>r.Claim.User).Where(r=>r.Status=="Pending").OrderBy(r=>r.Id).Take(100).ToList();
            var ids=requests.Select(r=>r.Id).ToArray();ViewBag.Evidence=Db.SkillEvidence.Where(e=>ids.Contains(e.SkillEvidenceRequestId)).ToList();return View(requests);
        }
        private ActionResult Run(Action action,string target="Index")
        {
            try{action();TempData["Notice"]="Đã cập nhật kỹ năng / yêu cầu xác minh.";}
            catch(InvalidOperationException ex){TempData["Error"]=ex.Message;}
            catch(System.Data.Entity.Infrastructure.DbUpdateException){TempData["Error"]="Dữ liệu đang được xử lý hoặc đã có yêu cầu chờ.";}
            catch(System.Data.SqlClient.SqlException){TempData["Error"]="Hệ thống đang xử lý đồng thời, hãy tải lại.";}
            return RedirectToAction(target);
        }
        [HttpPost,ValidateAntiForgeryToken]
        public ActionResult SaveClaim(int skillId,int level,int years)
        {if(CurrentUser.Role!="Freelancer")return Forbidden();if(!ModelState.IsValid)return new HttpStatusCodeResult(400);return Run(()=>Evidence.SaveClaim(CurrentUser.Id,skillId,level,years));}
        [HttpPost,ValidateAntiForgeryToken]
        public ActionResult DeleteClaim(int id)
        {if(CurrentUser.Role!="Freelancer")return Forbidden();return Run(()=>Evidence.DeleteClaim(id,CurrentUser.Id));}
        [HttpPost,ValidateAntiForgeryToken]
        public ActionResult Submit(int claimId,List<EvidenceInput> evidence)
        {if(CurrentUser.Role!="Freelancer")return Forbidden();if(!ModelState.IsValid)return new HttpStatusCodeResult(400);return Run(()=>Evidence.Submit(claimId,CurrentUser.Id,evidence));}
        [HttpPost,ValidateAntiForgeryToken]
        public ActionResult Review(int id,bool approve,string reason)
        {if(CurrentUser.Role!="Admin")return Forbidden();return Run(()=>Evidence.Review(id,CurrentUser.Id,approve,reason),"Admin");}
    }
}
