using System;
using System.Data.Entity;
using System.Linq;
using System.Web;
using System.Web.Script.Serialization;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Services
{
    public sealed class RealtimeAccessException : Exception
    {
        public int Status { get; private set; }
        public RealtimeAccessException(int status) { Status = status; }
    }
    public sealed class RealtimeService : IDisposable
    {
        private readonly AppDbContext db = new AppDbContext();
        private readonly UserAccount actor;
        private readonly int? conversationId, workspaceId;
        public bool CanSend { get; private set; }
        public RealtimeService(HttpRequestBase request, int? conversationId, int? workspaceId)
        {
            try
            {
                var id = AuthSessionService.AccountId(request);
                actor = id.HasValue ? db.Users.AsNoTracking().SingleOrDefault(u => u.Id == id.Value) : null;
                if (actor == null || actor.IsSuspended || !AuthSessionService.IsCurrent(request, actor) ||
                    (PasswordRecoveryService.RequireEmailVerification && !actor.EmailVerifiedAtUtc.HasValue)) throw new RealtimeAccessException(401);
                if ((conversationId.HasValue && conversationId <= 0) || (workspaceId.HasValue && workspaceId <= 0) ||
                    (conversationId.HasValue && workspaceId.HasValue)) throw new RealtimeAccessException(400);
                this.conversationId = conversationId; this.workspaceId = workspaceId;
                if (conversationId.HasValue)
                {
                    if (!ConversationService.CanMessage(actor)) throw new RealtimeAccessException(403);
                    var conversation = ConversationService.Owned(db.DirectConversations.Include(c => c.Client).Include(c => c.Freelancer), actor.Id)
                        .SingleOrDefault(c => c.Id == conversationId.Value);
                    if (conversation == null) throw new RealtimeAccessException(404);
                    CanSend = ConversationService.CanMessage(conversation.Client) && ConversationService.CanMessage(conversation.Freelancer);
                }
                if (workspaceId.HasValue)
                {
                    var workspace = db.Workspaces.Include(w => w.Project).SingleOrDefault(w => w.Id == workspaceId.Value);
                    var contracts = new ContractService(db);
                    if (workspace == null || (actor.Role != "Admin" && !contracts.CanAccess(workspace, actor.Id))) throw new RealtimeAccessException(404);
                    CanSend = actor.Role != "Admin" && contracts.CanAccess(workspace, actor.Id) && contracts.CanDiscuss(workspace);
                }
            }
            catch { db.Dispose(); throw; }
        }
        private IQueryable<DirectMessage> DirectMessages => db.DirectMessages.AsNoTracking().Where(m => m.ConversationId == conversationId);
        private IQueryable<WorkspaceMessage> WorkspaceMessages => db.WorkspaceMessages.AsNoTracking().Where(m => m.WorkspaceId == workspaceId);
        public object State()
        {
            var notifications = db.Notifications.AsNoTracking().Where(n => n.RecipientId == actor.Id);
            var unread = notifications.Count(n => !n.ReadAtUtc.HasValue);
            var recent = notifications.OrderByDescending(n => n.Id).Take(25).Select(n => new { n.Id, n.ReadAtUtc }).ToList();
            var owned = ConversationService.Owned(db.DirectConversations, actor.Id);
            var conversationIds = owned.Select(c => c.Id);
            var messageIds = ConversationService.CanMessage(actor) ? db.DirectMessages.Where(m => conversationIds.Contains(m.ConversationId))
                .OrderByDescending(m => m.Id).Take(50).Select(m => m.Id).ToArray() : new int[0];
            var latest = conversationId.HasValue ? DirectMessages.Select(m => (int?)m.Id).Max() ?? 0 :
                workspaceId.HasValue ? WorkspaceMessages.Select(m => (int?)m.Id).Max() ?? 0 : 0;
            var serializer = new JavaScriptSerializer();
            return new { accountId = actor.Id, unreadCount = unread,
                notificationsVersion = ContractService.Digest(serializer.Serialize(new { unread, total = notifications.Count(), recent })),
                conversationsVersion = ContractService.Digest(serializer.Serialize(new { count = owned.Count(), max = owned.Select(c => (int?)c.Id).Max() ?? 0, messageIds })),
                channelVersion = latest + ":" + CanSend, latestMessageId = latest, canSend = CanSend };
        }
        public object Messages(int after)
        {
            if (after < 0 || (!conversationId.HasValue && !workspaceId.HasValue)) throw new RealtimeAccessException(400);
            // IDs are persisted in SQL. Writer locks serialize commits within each channel.
            var query = conversationId.HasValue ? DirectMessages.Select(m => new MessageRow { id = m.Id, authorId = m.SenderId,
                author = m.Sender.Name, body = m.Body, createdAt = m.CreatedAtUtc }) :
                WorkspaceMessages.Select(m => new MessageRow { id = m.Id, authorId = m.AuthorId, author = m.Author.Name, body = m.Body, createdAt = m.CreatedAtUtc });
            var rows = after == 0 ? query.OrderByDescending(m => m.id).Take(50).ToList().OrderBy(m => m.id).ToList() :
                query.Where(m => m.id > after).OrderBy(m => m.id).Take(51).ToList();
            var more = rows.Count > 50; var batch = rows.Take(50).ToList();
            return new { accountId = actor.Id, canSend = CanSend, hasMore = more,
                nextCursor = batch.Count == 0 ? after : batch.Last().id,
                items = batch.Select(m => new { m.id, m.authorId, m.author, m.body,
                    createdAtUtc = DateTime.SpecifyKind(m.createdAt, DateTimeKind.Utc).ToString("o") }).ToArray() };
        }
        private sealed class MessageRow
        {
            public int id { get; set; }
            public int authorId { get; set; }
            public string author { get; set; }
            public string body { get; set; }
            public DateTime createdAt { get; set; }
        }
        public void Dispose() { db.Dispose(); }
    }
}
