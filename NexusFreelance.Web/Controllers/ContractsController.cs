using System;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;
namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public sealed class ContractsController : BaseController
    {
        private ContractService Service => new ContractService(Db);
        [HttpGet]
        public ActionResult Index(int? projectId, string stage = "")
        {
            if(projectId.HasValue && projectId.Value<=0)return new HttpStatusCodeResult(400);
            stage=stage??"";
            if(!ContractInbox.Stages.Any(s=>s.Key==stage))return new HttpStatusCodeResult(400);
            var user=CurrentUser;var query=ContractInbox.VisibleTo(Db,user.Id,user.Role).Include(c=>c.Project).Include(c=>c.Counterparty);
            if(projectId.HasValue) query=query.Where(c=>c.ProjectId==projectId);
            query=ContractInbox.Filter(Db,query,stage,user.Id,user.Role=="Admin");
            var contracts=query.OrderByDescending(c=>c.Id).Take(100).ToList();var ids=contracts.Select(c=>c.Id).ToArray();
            ViewBag.Versions=Db.ContractVersions.Where(v=>ids.Contains(v.ContractId) && v.VersionNumber==v.Contract.CurrentVersion).ToDictionary(v=>v.ContractId);
            ViewBag.ProjectId=projectId;ViewBag.Stage=stage;return View(contracts);
        }
        [HttpGet]
        public ActionResult Details(int id,int? version)
        {
            var c=Db.Contracts.Include(x=>x.Project.Client).Include(x=>x.Counterparty).SingleOrDefault(x=>x.Id==id);if(c==null)return HttpNotFound();
            if(CurrentUser.Role!="Admin" && !Service.CanView(c,CurrentUser.Id))return Forbidden();
            var v=version.HasValue?Db.ContractVersions.SingleOrDefault(x=>x.ContractId==id && x.VersionNumber==version):Service.Current(c);
            if(v==null)return HttpNotFound();
            var payments=Db.PaymentIntents.Where(p=>p.ContractId==id).OrderByDescending(p=>p.Id).Take(50).ToList();
            var page=new ContractDetailsPage {Contract=c,Version=v,Snapshot=ContractService.ReadSnapshot(v),IsOwner=c.Project.ClientId==CurrentUser.Id,IsAdmin=CurrentUser.Role=="Admin",
                Acceptances=Db.ContractAcceptances.Include(a=>a.User).Where(a=>a.ContractVersionId==v.Id).OrderBy(a=>a.AcceptedAtUtc).ToList(),
                Milestones=Db.EscrowMilestones.Where(m=>m.ContractId==id).OrderBy(m=>m.Id).ToList(),Payments=payments,
                Refunds=Db.PspRefunds.Where(r=>r.Payment.ContractId==id).OrderByDescending(r=>r.Id).ToList(),
                Disputes=Db.EscrowDisputes.Where(d=>d.ContractId==id).OrderByDescending(d=>d.Id).ToList(),
                Versions=Db.ContractVersions.Where(x=>x.ContractId==id).OrderByDescending(x=>x.VersionNumber).ToList(),WorkspaceId=Db.Workspaces.Where(w=>w.ContractId==id).Select(w=>w.Id).Single()};
            page.HasConfirmed=page.Acceptances.Any(a=>a.UserAccountId==CurrentUser.Id && a.AcceptedHash==v.Hash);
            page.CanRevise=page.IsOwner && v.VersionNumber==c.CurrentVersion && (c.Status=="AwaitingSignatures" || c.Status=="AwaitingEscrow") && payments.Count==0 && page.Milestones.All(m=>m.Status=="Planned");
            return View(page);
        }
        private ActionResult Command(int id,Action command)
        {
            try {command();TempData["Notice"]="Đã ghi nhận thay đổi hợp đồng.";return RedirectToAction("Details",new{id});}
            catch(InvalidOperationException ex){return new HttpStatusCodeResult(409,ex.Message);}
            catch(System.Data.Entity.Infrastructure.DbUpdateException){return new HttpStatusCodeResult(409,"Dữ liệu đã thay đổi hoặc thao tác đã được ghi nhận.");}
            catch(System.Data.SqlClient.SqlException){return new HttpStatusCodeResult(409,"Hợp đồng đang được xử lý; hãy tải lại.");}
        }
        [HttpPost,ValidateAntiForgeryToken]
        public ActionResult Confirm(int id,int versionId,string hash)
        {
            var c=Db.Contracts.Include(x=>x.Project).SingleOrDefault(x=>x.Id==id);if(c==null)return HttpNotFound();
            if(!Service.CanView(c,CurrentUser.Id))return Forbidden();
            if(!ModelState.IsValid || string.IsNullOrEmpty(hash) || hash.Length!=64 || versionId<=0)return new HttpStatusCodeResult(400);
            return Command(id,()=>Service.Confirm(id,CurrentUser.Id,versionId,hash));
        }
        [HttpPost,ValidateAntiForgeryToken]
        public ActionResult Revise(int id,string scope,string milestonePlan,int deliveryDays)
        {
            var c=Db.Contracts.Include(x=>x.Project).SingleOrDefault(x=>x.Id==id);if(c==null)return HttpNotFound();
            if(c.Project.ClientId!=CurrentUser.Id)return Forbidden();if(!ModelState.IsValid)return new HttpStatusCodeResult(400);
            return Command(id,()=>Service.Revise(id,CurrentUser.Id,scope,milestonePlan,deliveryDays));
        }
        [HttpPost,ValidateAntiForgeryToken]
        public ActionResult Decline(int id,string reason)
        {
            var c=Db.Contracts.Include(x=>x.Project).SingleOrDefault(x=>x.Id==id);if(c==null)return HttpNotFound();
            if(!Service.CanView(c,CurrentUser.Id))return Forbidden();if(!ModelState.IsValid)return new HttpStatusCodeResult(400);
            return Command(id,()=>Service.Decline(id,CurrentUser.Id,reason));
        }
    }
}
