using System;
using System.Linq;
using System.Web;
using System.Web.Caching;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Services
{
    public sealed class ConversationSuggestion
    {
        public int UserId { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }
    }

    public sealed class ConversationService
    {
        private readonly AppDbContext db;
        public ConversationService(AppDbContext db) { this.db = db; }

        public static bool CanMessage(UserAccount user) => user != null && !user.IsSuspended &&
            (user.Role == "Client" || user.Role == "Freelancer");

        // These predicates remain IQueryable so eligibility is checked by SQL, including at POST time.
        public static IQueryable<UserAccount> RelatedContacts(UserAccount actor, IQueryable<UserAccount> users,
            IQueryable<FreelancerGroupMember> members, IQueryable<RoleAssignment> assignments,
            IQueryable<TeamMember> teamMembers, IQueryable<ProjectContract> contracts,
            IQueryable<ContractMember> contractMembers)
        {
            if (!CanMessage(actor) || actor.Role != "Freelancer") return users.Where(u => false);
            var userId = actor.Id;
            return users.Where(u => u.Id != userId && !u.IsSuspended && (
                (u.Role == "Freelancer" && members.Any(peer => peer.UserAccountId == u.Id && peer.Status == "Active" &&
                    members.Any(own => own.UserAccountId == userId && own.Status == "Active" && own.FreelancerGroupId == peer.FreelancerGroupId))) ||
                (u.Role == "Client" && (
                    assignments.Any(a => a.FreelancerId == userId && a.Project.ClientId == u.Id) ||
                    teamMembers.Any(m => m.UserAccountId == userId && m.Team.Project.ClientId == u.Id) ||
                    contracts.Any(c => c.CounterpartyId == userId && c.Project.ClientId == u.Id) ||
                    contractMembers.Any(m => m.UserAccountId == userId && m.Contract.Project.ClientId == u.Id)))));
        }

        public IQueryable<UserAccount> RelatedContacts(UserAccount actor) => RelatedContacts(actor, db.Users,
            db.FreelancerGroupMembers, db.RoleAssignments, db.TeamMembers, db.Contracts, db.ContractMembers);

        public IQueryable<ConversationSuggestion> Suggestions(UserAccount actor)
        {
            var userId = actor.Id;
            return RelatedContacts(actor).Where(u => !db.DirectConversations.Any(c =>
                (c.ClientId == userId && c.FreelancerId == u.Id) || (c.FreelancerId == userId && c.ClientId == u.Id)))
                .OrderBy(u => u.Name).ThenBy(u => u.Id).Take(24)
                .Select(u => new ConversationSuggestion { UserId = u.Id, Name = u.Name, Role = u.Role });
        }

        public static IQueryable<DirectConversation> Owned(IQueryable<DirectConversation> rows, int userId) =>
            rows.Where(c => c.ClientId == userId || c.FreelancerId == userId);

        public static IQueryable<DirectConversation> Pair(IQueryable<DirectConversation> rows, int first, int second) =>
            rows.Where(c => (c.ClientId == first && c.FreelancerId == second) || (c.ClientId == second && c.FreelancerId == first));

        public static DirectConversation CreatePair(UserAccount actor, UserAccount recipient)
        {
            if (!CanMessage(actor) || !CanMessage(recipient) || actor.Id == recipient.Id ||
                (actor.Role == "Client" && recipient.Role == "Client")) return null;
            // Historical FK names stay intact. Only colleague pairs use ascending user IDs.
            var first = actor.Role == "Client" ? actor.Id : recipient.Role == "Client" ? recipient.Id : Math.Min(actor.Id, recipient.Id);
            var second = first == actor.Id ? recipient.Id : actor.Id;
            return new DirectConversation { ClientId = first, FreelancerId = second, CreatedAtUtc = DateTime.UtcNow };
        }

        public static bool TryMessage(string raw, out string body)
        {
            body = null;
            if (raw == null || raw.Length > 2000) return false;
            body = raw.Trim();
            return body.Length > 0;
        }

        private sealed class OpeningWindow { public int Count; }
        private static readonly object openingGate = new object();
        // Short-lived per-account request budget; send quotas additionally use the database.
        // Cache expiration/recycling resets this opening budget; no identity or content is cached.
        public static bool TryOpen(int userId)
        {
            lock (openingGate)
            {
                var key = "conversation-open:" + userId;
                var window = HttpRuntime.Cache[key] as OpeningWindow;
                if (window == null)
                {
                    window = new OpeningWindow();
                    HttpRuntime.Cache.Insert(key, window, null, DateTime.UtcNow.AddMinutes(1), Cache.NoSlidingExpiration);
                }
                if (window.Count >= 10) return false;
                window.Count++;
                return true;
            }
        }
    }
}
