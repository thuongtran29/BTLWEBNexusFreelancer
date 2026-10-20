using System;
using System.Linq;
using System.Data.Entity;
using System.Web.Mvc;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public class NotificationsController : BaseController
    {
        [HttpGet]
        public ActionResult Index(int page = 1)
        {
            if (CurrentUser == null) return Forbidden();
            if (page < 1 || page > 100000) return new HttpStatusCodeResult(400);
            const int pageSize = 25;
            ViewBag.Page = page;
            ViewBag.HasNext = Db.Notifications.Count(n => n.RecipientId == CurrentUser.Id) > page * pageSize;
            return View(Db.Notifications.Include(n => n.Actor).Include(n => n.Project).Where(n => n.RecipientId == CurrentUser.Id)
                .OrderByDescending(n => n.Id).Skip((page - 1) * pageSize).Take(pageSize).ToList());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Open(int id)
        {
            if (CurrentUser == null) return Forbidden();
            var notification = Db.Notifications.SingleOrDefault(n => n.Id == id && n.RecipientId == CurrentUser.Id);
            if (notification == null) return HttpNotFound();
            if (!notification.ReadAtUtc.HasValue) { notification.ReadAtUtc = DateTime.UtcNow; Db.SaveChanges(); }
            return Url.IsLocalUrl(notification.TargetPath) ? (ActionResult)Redirect(notification.TargetPath) : RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult MarkAllRead()
        {
            if (CurrentUser == null) return Forbidden();
            Db.Database.ExecuteSqlCommand("UPDATE dbo.Notifications SET ReadAtUtc=@p0 WHERE RecipientId=@p1 AND ReadAtUtc IS NULL", DateTime.UtcNow, CurrentUser.Id);
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult MarkRead(int id)
        {
            if (CurrentUser == null) return Forbidden();
            var notification = Db.Notifications.SingleOrDefault(n => n.Id == id && n.RecipientId == CurrentUser.Id);
            if (notification == null) return HttpNotFound();
            if (!notification.ReadAtUtc.HasValue)
            {
                notification.ReadAtUtc = DateTime.UtcNow;
                Db.SaveChanges();
            }
            return RedirectToAction("Index");
        }
    }
}
