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
    public class NegotiationController : BaseController
    {
        private Proposal Canonical(int proposalId)
        {
            var p = Db.Proposals.Include(x => x.Project).Include(x => x.Applicant).Include(x => x.GroupBid).SingleOrDefault(x => x.Id == proposalId);
            if (p == null) return null;
            if (p.GroupBidId.HasValue) return Db.Proposals.Include(x => x.Project).Include(x => x.Applicant).Include(x => x.GroupBid)
                .Where(x => x.GroupBidId == p.GroupBidId).OrderBy(x => x.Id).First();
            return p;
        }
        private bool CanRead(Proposal p) => p != null && (p.Project.ClientId == CurrentUser.Id ||
            (p.GroupBidId.HasValue ? p.GroupBid.LeaderId == CurrentUser.Id : p.ApplicantId == CurrentUser.Id));
        [HttpGet]
        public ActionResult Thread(int proposalId, int? beforeId)
        {
            var p = Canonical(proposalId); if (p == null) return HttpNotFound(); if (!CanRead(p)) return Forbidden();
            var query = Db.NegotiationMessages.Include(m => m.Sender).Where(m => m.ProposalId == p.Id);
            if (beforeId.HasValue) { if (beforeId.Value < 1) return new HttpStatusCodeResult(400); query = query.Where(m => m.Id < beforeId.Value); }
            var messages = query.OrderByDescending(m => m.Id).Take(50).ToList(); messages.Reverse();
            ViewBag.Messages = messages; ViewBag.ParticipantName = p.GroupBidId.HasValue ? Db.Users.Where(u => u.Id == p.GroupBid.LeaderId).Select(u => u.Name).First() : p.Applicant.Name;
            ViewBag.BidMembers = p.GroupBidId.HasValue
                ? Db.Proposals.Include(x => x.Applicant).Include(x => x.Requirement).Where(x => x.GroupBidId == p.GroupBidId).OrderBy(x => x.Id).ToList()
                : new System.Collections.Generic.List<Proposal>();
            return View(p);
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Send(int proposalId, string body)
        {
            if (string.IsNullOrWhiteSpace(body) || body.Length > 2000) return new HttpStatusCodeResult(400);
            var parent = Db.Proposals.Where(p => p.Id == proposalId).Select(p => (int?)p.ProjectId).SingleOrDefault(); if (!parent.HasValue) return HttpNotFound();
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                Db.Database.SqlQuery<int>("SELECT Id FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", parent.Value).Single();
                var p = Canonical(proposalId); if (!CanRead(p)) return Forbidden();
                if (p.Project.Status != "Published" || (p.Status != "Pending" && p.Status != "Shortlisted")) return new HttpStatusCodeResult(409, "Trao đổi trước hợp đồng đã đóng. Dùng Workspace khi dự án bắt đầu.");
                Db.NegotiationMessages.Add(new NegotiationMessage { ProposalId = p.Id, SenderId = CurrentUser.Id, Body = body.Trim(), CreatedAtUtc = DateTime.UtcNow });
                var participant = p.GroupBidId.HasValue ? p.GroupBid.LeaderId : p.ApplicantId;
                NotificationService.Queue(Db, CurrentUser.Id == p.Project.ClientId ? participant : p.Project.ClientId, "Tin nhắn trước hợp đồng: " + p.Project.Title.Substring(0, Math.Min(120,p.Project.Title.Length)), "/Negotiation/Thread?proposalId=" + p.Id);
                Db.SaveChanges(); tx.Commit();
            }
            return RedirectToAction("Thread", new { proposalId });
        }
    }
}
