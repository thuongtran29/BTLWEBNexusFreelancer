using System;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;
namespace NexusFreelance.Web.Controllers
{
 [Authorize]
 public class KnowledgeController:BaseController
 {
  private SharedKnowledgeService Library=>new SharedKnowledgeService(Db);
  [HttpGet] public ActionResult Index(string q="",string industry="",int page=1) {
   q=(q??"").Trim();if(!ModelState.IsValid || q.Length>100 || !IndustryCatalog.IsValid(industry) || page<1 || page>100000)return new HttpStatusCodeResult(400);
   var query=Library.Available();if(q.Length>0)query=query.Where(k=>k.Title.Contains(q)||k.Summary.Contains(q)||k.SkillTags.Contains(q));if(!string.IsNullOrWhiteSpace(industry))query=query.Where(k=>k.Industry==industry);
   var total=query.Count();page=Math.Min(page,Math.Max(1,(total+11)/12));
   return View(new KnowledgeLibraryPage {Query=q,Industry=industry,Page=page,Total=total,Items=query.OrderByDescending(k=>k.CreatedAtUtc).ThenByDescending(k=>k.Id).Skip((page-1)*12).Take(12).Select(k=>new SharedKnowledgeCard{Id=k.Id,Version=k.Version,Title=k.Title,Industry=k.Industry,Summary=k.Summary,SkillTags=k.SkillTags,CreatedAtUtc=k.CreatedAtUtc}).ToList()});
  }
  [HttpGet] public ActionResult Details(int id) {
   var item=Library.Available().Where(k=>k.Id==id).Select(k=>new SharedKnowledgeCard {Id=k.Id,Version=k.Version,Title=k.Title,Industry=k.Industry,Summary=k.Summary,Practices=k.Practices,AcceptanceChecklist=k.AcceptanceChecklist,SkillTags=k.SkillTags,CreatedAtUtc=k.CreatedAtUtc}).SingleOrDefault();if(item==null)return HttpNotFound();
   ViewBag.CanWithdraw=CurrentUser.Role=="Admin" || Db.SharedKnowledgeEntries.Any(k=>k.Id==id && k.AuthorId==CurrentUser.Id);return View(item);
  }
  [HttpGet] public ActionResult Completed(int page=1) {
   if(CurrentUser.Role!="Client" && CurrentUser.Role!="Admin")return Forbidden();if(page<1 || page>100000)return new HttpStatusCodeResult(400);
   var query=Db.Projects.Where(p=>p.Status=="Completed" && (p.ClientId==CurrentUser.Id || CurrentUser.Role=="Admin"));var total=query.Count();page=Math.Min(page,Math.Max(1,(total+11)/12));ViewBag.Page=page;ViewBag.Total=total;
   return View(query.OrderByDescending(p=>p.Id).Skip((page-1)*12).Take(12).Select(p=>new CompletedKnowledgeProject {Id=p.Id,Title=p.Title,Industry=p.Industry,IsOwner=p.ClientId==CurrentUser.Id,MemoryVersions=Db.ProjectMemories.Count(m=>m.ProjectId==p.Id),HasSharedArticle=Db.SharedKnowledgeEntries.Any(k=>k.ProjectId==p.Id && k.Status=="Published")}).ToList());
  }
  [HttpGet] public ActionResult Share(int projectId) {
   var p=Db.Projects.SingleOrDefault(x=>x.Id==projectId);if(p==null)return HttpNotFound();if(CurrentUser.Role!="Client" || p.ClientId!=CurrentUser.Id)return Forbidden();if(p.Status!="Completed")return new HttpStatusCodeResult(409);
   ViewBag.Project=p;var last=Db.SharedKnowledgeEntries.Where(k=>k.ProjectId==p.Id).OrderByDescending(k=>k.Version).FirstOrDefault();
   return View(new KnowledgePublishForm {ProjectId=p.Id,ExpectedVersion=last?.Version??0,Title=last?.Title,Summary=last?.Summary,Practices=last?.Practices,AcceptanceChecklist=last?.AcceptanceChecklist,SkillTags=last?.SkillTags});
  }
  [HttpPost,ValidateAntiForgeryToken] public ActionResult Share(KnowledgePublishForm form) {
   if(CurrentUser.Role!="Client")return Forbidden();if(form==null)return new HttpStatusCodeResult(400);var p=Db.Projects.SingleOrDefault(x=>x.Id==form.ProjectId);if(p==null)return HttpNotFound();if(p.ClientId!=CurrentUser.Id)return Forbidden();if(p.Status!="Completed")return new HttpStatusCodeResult(409);
   if(!form.ConfirmGeneric)ModelState.AddModelError("ConfirmGeneric","Xác nhận đã loại bỏ thông tin riêng của cá nhân và dự án.");
   if(ModelState.IsValid)try{var id=Library.Publish(CurrentUser.Id,form);TempData["Notice"]="Đã chia sẻ bài học chung. AI có thể tham khảo khi người soạn JD bật nguồn này.";return RedirectToAction("Details",new{id});}catch(InvalidOperationException ex){ModelState.AddModelError("",ex.Message);}
   ViewBag.Project=p;return View(form);
  }
  [HttpPost,ValidateAntiForgeryToken] public ActionResult Withdraw(int id,string reason) {
   try{Library.Withdraw(id,CurrentUser.Id,reason);TempData["Notice"]="Đã ngừng chia sẻ; nguồn này không dùng cho lần tạo JD mới.";}catch(UnauthorizedAccessException){return Forbidden();}catch(InvalidOperationException ex){TempData["Error"]=ex.Message;}return RedirectToAction("Index");
  }
 }
}
