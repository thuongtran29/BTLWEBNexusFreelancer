using System;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public sealed class MessagesController : BaseController
    {
        [HttpGet]
        public ActionResult Index()
        {
            if (!ConversationService.CanMessage(CurrentUser)) return Forbidden();
            ViewBag.Suggestions = new ConversationService(Db).Suggestions(CurrentUser).ToList();
            var rows = ConversationService.Owned(Db.DirectConversations.Include(c => c.Client).Include(c => c.Freelancer), CurrentUser.Id)
                .OrderByDescending(c => Db.DirectMessages.Where(m => m.ConversationId == c.Id).Select(m => (DateTime?)m.CreatedAtUtc).Max() ?? c.CreatedAtUtc)
                .ThenByDescending(c => c.Id).Take(100).ToList();
            var ids = rows.Select(c => c.Id).ToArray();
            ViewBag.LatestMessages = Db.DirectMessages.AsNoTracking().Where(m => ids.Contains(m.ConversationId) &&
                !Db.DirectMessages.Any(next => next.ConversationId == m.ConversationId && next.Id > m.Id)).ToList().ToDictionary(m => m.ConversationId);
            return View(rows);
        }
        // Keep the existing public freelancer-profile forms and their parameter intact.
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Contact(int freelancerId)
        {
            if (!ConversationService.CanMessage(CurrentUser) || CurrentUser.Role != "Client") return Forbidden();
            return Open(freelancerId, true);
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Start(int recipientId)
        {
            if (!ConversationService.CanMessage(CurrentUser)) return Forbidden();
            return Open(recipientId, false);
        }
        private ActionResult Open(int recipientId, bool publicProfileContact)
        {
            if (!ModelState.IsValid || recipientId <= 0 || recipientId == CurrentUser.Id) return new HttpStatusCodeResult(400);
            if (!ConversationService.TryOpen(CurrentUser.Id)) return new HttpStatusCodeResult(429);
            int id;
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.RepeatableRead))
            {
                // Both initiation directions lock the same rows in ascending order to protect pair reuse.
                foreach (var userId in new[] { CurrentUser.Id, recipientId }.OrderBy(value => value))
                    Db.Database.SqlQuery<int>("SELECT Id FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", userId).ToList();
                var recipient = Db.Users.SingleOrDefault(u => u.Id == recipientId);
                if (!ConversationService.CanMessage(recipient)) return HttpNotFound();
                var canContactProfile = CurrentUser.Role == "Client" && recipient.Role == "Freelancer" &&
                    Db.FreelancerProfiles.Any(p => p.UserAccountId == recipientId && p.IsPublic != false);
                if (publicProfileContact && !canContactProfile) return HttpNotFound();
                var conversation = ConversationService.Pair(Db.DirectConversations, CurrentUser.Id, recipientId).OrderBy(c => c.Id).FirstOrDefault();
                if (conversation == null)
                {
                    // Submitted suggestion IDs never grant permission: recheck persisted relationships.
                    if (!canContactProfile && !new ConversationService(Db).RelatedContacts(CurrentUser).Any(u => u.Id == recipientId)) return HttpNotFound();
                    conversation = ConversationService.CreatePair(CurrentUser, recipient);
                    if (conversation == null) return HttpNotFound();
                    Db.DirectConversations.Add(conversation); Db.SaveChanges();
                }
                id = conversation.Id; tx.Commit();
            }
            return RedirectToAction("Thread", new { id });
        }
        private DirectConversation Owned(int id) => ConversationService.Owned(
            Db.DirectConversations.Include(c => c.Client).Include(c => c.Freelancer), CurrentUser.Id).SingleOrDefault(c => c.Id == id);
        [HttpGet]
        public ActionResult Thread(int id, int? before)
        {
            if (!ConversationService.CanMessage(CurrentUser)) return Forbidden();
            if (id <= 0 || (before.HasValue && before <= 0)) return new HttpStatusCodeResult(400);
            var conversation = Owned(id); if (conversation == null) return HttpNotFound();
            var query = Db.DirectMessages.Include(m => m.Sender).Where(m => m.ConversationId == id);
            if (before.HasValue) query = query.Where(m => m.Id < before.Value);
            ViewBag.Messages = query.OrderByDescending(m => m.Id).Take(50).ToList().OrderBy(m => m.Id).ToList();
            ViewBag.CanSend = ConversationService.CanMessage(conversation.Client) && ConversationService.CanMessage(conversation.Freelancer);
            return View(conversation);
        }
        [HttpPost, ValidateAntiForgeryToken, ValidateInput(false)]
        public ActionResult Send(int id, string body)
        {
            if (!ConversationService.CanMessage(CurrentUser)) return Forbidden();
            if (id <= 0) return new HttpStatusCodeResult(400);
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                // Serialize each sender's quota check and insert across concurrent requests.
                Db.Database.SqlQuery<int>("SELECT Id FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", CurrentUser.Id).Single();
                // Commit order is also message cursor order within a private channel.
                Db.Database.SqlQuery<int>("SELECT Id FROM dbo.DirectConversations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", id).ToList();
                var conversation = Owned(id); if (conversation == null) return HttpNotFound();
                if (!ConversationService.CanMessage(conversation.Client) || !ConversationService.CanMessage(conversation.Freelancer)) return Forbidden();
                string normalized;
                if (!ModelState.IsValid || !ConversationService.TryMessage(body, out normalized))
                {
                    if (Request.IsAjaxRequest()) { Response.StatusCode = 422; return Json(new { error = "Nhập tin nhắn từ 1 đến 2000 ký tự." }); }
                    TempData["Error"] = "Nhập tin nhắn từ 1 đến 2000 ký tự.";
                    return RedirectToAction("Thread", new { id });
                }
                var since = DateTime.UtcNow.AddMinutes(-1);
                if (Db.DirectMessages.Count(m => m.SenderId == CurrentUser.Id && m.CreatedAtUtc >= since) >= 15) return new HttpStatusCodeResult(429);
                Db.DirectMessages.Add(new DirectMessage { ConversationId = id, SenderId = CurrentUser.Id, Body = normalized, CreatedAtUtc = DateTime.UtcNow });
                var recipientId = conversation.ClientId == CurrentUser.Id ? conversation.FreelancerId : conversation.ClientId;
                NotificationService.Queue(Db, recipientId, "Tin nhắn mới từ " + CurrentUser.Name, "/Messages/Thread/" + id, CurrentUser.Id);
                Db.SaveChanges();
                tx.Commit();
            }
            if (Request.IsAjaxRequest()) return Json(new { sent = true });
            return RedirectToAction("Thread", new { id });
        }
    }
}
