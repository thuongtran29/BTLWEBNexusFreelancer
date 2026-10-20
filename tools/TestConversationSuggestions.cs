// Source-level harness: compiles only the owned messaging sources, never the web project.
using System;
using System.Collections.Generic;
using System.Collections;
using System.Collections.Specialized;
using System.Data.Entity;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Web;
using System.Web.Mvc;
using NexusFreelance.Web.Controllers;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;

internal static class TestConversationSuggestions
{
    private static int checks;
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    private static UserAccount User(int id, string role = "Freelancer") { return new UserAccount { Id = id, Name = "Person " + id, Role = role }; }
    private sealed class MemorySet<T> : DbSet<T>, IQueryable<T> where T : class
    {
        private readonly IQueryable<T> query;
        public MemorySet(IEnumerable<T> rows) { query = rows.AsQueryable(); }
        Type IQueryable.ElementType { get { return query.ElementType; } }
        Expression IQueryable.Expression { get { return query.Expression; } }
        IQueryProvider IQueryable.Provider { get { return query.Provider; } }
        IEnumerator<T> IEnumerable<T>.GetEnumerator() { return query.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return query.GetEnumerator(); }
    }
    private sealed class RequestStub : HttpRequestBase
    {
        public override string this[string key] { get { return null; } }
        public override NameValueCollection Headers { get { return new NameValueCollection(); } }
    }
    private sealed class ResponseStub : HttpResponseBase
    {
        public override int StatusCode { get; set; }
        public override bool TrySkipIisCustomErrors { get; set; }
    }
    private sealed class ContextStub : HttpContextBase
    {
        private readonly ResponseStub response = new ResponseStub();
        public override HttpRequestBase Request { get { return new RequestStub(); } }
        public override HttpResponseBase Response { get { return response; } }
    }
    private static MessagesController Controller(UserAccount user)
    {
        var controller = new MessagesController();
        typeof(BaseController).GetField("activeUser", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, user);
        typeof(BaseController).GetField("userLoaded", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);
        controller.ControllerContext = new ControllerContext(new ContextStub(), new System.Web.Routing.RouteData(), controller);
        return controller;
    }
    public static int Main(string[] args)
    {
        try
        {
            var actor = User(10);
            var users = new[] { actor, User(20), User(21), User(22), User(23), User(24), User(25), User(26), User(27), User(28), User(29, "Admin"), User(30, "Client"), User(31, "Client"), User(32, "Client"), User(33, "Client"), User(34, "Client"), User(35, "Client") };
            users.Single(u => u.Id == 28).IsSuspended = true;
            var members = new List<FreelancerGroupMember> {
                new FreelancerGroupMember { FreelancerGroupId = 1, UserAccountId = 10, Status = "Active" },
                new FreelancerGroupMember { FreelancerGroupId = 1, UserAccountId = 20, Status = "Active" },
                new FreelancerGroupMember { FreelancerGroupId = 1, UserAccountId = 21, Status = "Invited" },
                new FreelancerGroupMember { FreelancerGroupId = 1, UserAccountId = 22, Status = "Left" },
                new FreelancerGroupMember { FreelancerGroupId = 1, UserAccountId = 23, Status = "Declined" },
                new FreelancerGroupMember { FreelancerGroupId = 2, UserAccountId = 24, Status = "Active" },
                new FreelancerGroupMember { FreelancerGroupId = 2, UserAccountId = 10, Status = "Invited" },
                new FreelancerGroupMember { FreelancerGroupId = 1, UserAccountId = 28, Status = "Active" },
                new FreelancerGroupMember { FreelancerGroupId = 1, UserAccountId = 29, Status = "Active" }
            };
            var assignments = new[] { new RoleAssignment { FreelancerId = 10, Project = new Project { ClientId = 30 } }, new RoleAssignment { FreelancerId = 25, Project = new Project { ClientId = 31 } } };
            var teams = new[] { new TeamMember { UserAccountId = 10, Team = new ProjectTeam { Project = new Project { ClientId = 32 } } } };
            var contracts = new[] { new ProjectContract { CounterpartyId = 10, Project = new Project { ClientId = 33 } } };
            var contractMembers = new[] { new ContractMember { UserAccountId = 10, Contract = new ProjectContract { Project = new Project { ClientId = 34 } } } };
            Func<int[]> related = () => ConversationService.RelatedContacts(actor, users.AsQueryable(), members.AsQueryable(), assignments.AsQueryable(), teams.AsQueryable(), contracts.AsQueryable(), contractMembers.AsQueryable()).Select(u => u.Id).OrderBy(i => i).ToArray();
            Check(related().SequenceEqual(new[] { 20, 30, 32, 33, 34 }), "Only active colleagues and actual project/contract Clients qualify; self, outsiders, pending/left members, admins and suspended users excluded");
            members.Add(new FreelancerGroupMember { FreelancerGroupId = 1, UserAccountId = 20, Status = "Active" });
            Check(related().Count(i => i == 20) == 1, "Multiple shared memberships do not duplicate suggestions");
            members[1].Status = "Left"; members[members.Count - 1].Status = "Left";
            Check(!related().Contains(20), "Relationship revoked before starting prevents new colleague conversation");
            Check(!ConversationService.RelatedContacts(User(1, "Client"), users.AsQueryable(), members.AsQueryable(), assignments.AsQueryable(), teams.AsQueryable(), contracts.AsQueryable(), contractMembers.AsQueryable()).Any(), "Client cannot use colleague suggestions to reach unrelated users");
            actor.IsSuspended = true; Check(!related().Any(), "Suspended sender has no eligible contacts"); actor.IsSuspended = false;

            var pair = ConversationService.CreatePair(actor, User(20));
            var reversed = ConversationService.CreatePair(User(20), actor);
            Check(pair.ClientId == 10 && pair.FreelancerId == 20 && reversed.ClientId == 10 && reversed.FreelancerId == 20, "Colleague pair canonical in both initiation directions");
            pair = ConversationService.CreatePair(actor, User(30, "Client"));
            Check(pair.ClientId == 30 && pair.FreelancerId == 10, "Legacy Client/Freelancer column meaning preserved regardless of sender");
            Check(ConversationService.CreatePair(actor, actor) == null && ConversationService.CreatePair(actor, User(29, "Admin")) == null, "Self and admin pairs rejected");
            var old = new DirectConversation { Id = 7, ClientId = 20, FreelancerId = 10 };
            var rows = new[] { old, new DirectConversation { Id = 8, ClientId = 30, FreelancerId = 10 } }.AsQueryable();
            Check(ConversationService.Pair(rows, 10, 20).Single() == old, "Reverse historical pair reused without rewriting history");
            Check(ConversationService.Owned(rows, 10).Count() == 2 && !ConversationService.Owned(rows, 99).Any(), "Only either original participant can read/send");
            using (var memory = new AppDbContext())
            {
                memory.Users = new MemorySet<UserAccount>(users);
                memory.FreelancerGroupMembers = new MemorySet<FreelancerGroupMember>(members);
                memory.RoleAssignments = new MemorySet<RoleAssignment>(assignments);
                memory.TeamMembers = new MemorySet<TeamMember>(teams);
                memory.Contracts = new MemorySet<ProjectContract>(contracts);
                memory.ContractMembers = new MemorySet<ContractMember>(contractMembers);
                memory.DirectConversations = new MemorySet<DirectConversation>(rows);
                Check(new ConversationService(memory).Suggestions(actor).Select(s => s.UserId).OrderBy(i => i).SequenceEqual(new[] { 32, 33, 34 }), "Actual suggestions exclude existing conversations and unrelated peers without exposing account fields");
            }
            string body;
            Check(ConversationService.TryMessage("  hello  ", out body) && body == "hello", "Message body normalized");
            Check(!ConversationService.TryMessage(null, out body) && !ConversationService.TryMessage(" \r\n ", out body), "Empty message rejected");
            Check(ConversationService.TryMessage(new string('x', 2000), out body) && !ConversationService.TryMessage(new string('x', 2001), out body), "Message length enforced at 2000 characters");
            foreach (var action in new[] { "Contact", "Start", "Send" })
            {
                var method = typeof(MessagesController).GetMethod(action);
                Check(method.IsDefined(typeof(HttpPostAttribute), true) && method.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true), action + " requires POST and CSRF token");
            }
            Check(ConversationService.CanMessage(actor) && !ConversationService.CanMessage(null) && !ConversationService.CanMessage(User(1, "Admin")), "Messaging role gate denies anonymous/admin actors");
            foreach (var denied in new[] { (UserAccount)null, User(1, "Admin"), new UserAccount { Id = 2, Role = "Freelancer", IsSuspended = true } })
            {
                using (var controller = Controller(denied))
                {
                    Check(controller.Index() is ViewResult && controller.Response.StatusCode == 403, "Index denies unavailable actor before data access");
                    Check(controller.Thread(7, null) is ViewResult && controller.Response.StatusCode == 403, "Thread denies unavailable actor before ownership query");
                    Check(controller.Send(7, "hello") is ViewResult && controller.Response.StatusCode == 403, "Send denies unavailable actor before transaction");
                    Check(controller.Start(20) is ViewResult && controller.Response.StatusCode == 403, "Start denies unavailable actor before transaction");
                }
            }
            using (var controller = Controller(actor))
            {
                Check(((HttpStatusCodeResult)controller.Start(actor.Id)).StatusCode == 400 && ((HttpStatusCodeResult)controller.Start(0)).StatusCode == 400, "Start rejects self/invalid recipient without data access");
                Check(controller.Contact(20) is ViewResult && controller.Response.StatusCode == 403, "Legacy Contact stays Client-only");
                Check(((HttpStatusCodeResult)controller.Thread(7, 0)).StatusCode == 400, "Invalid pagination rejected before reading messages");
                controller.ModelState.AddModelError("recipientId", "malformed");
                Check(((HttpStatusCodeResult)controller.Start(20)).StatusCode == 400, "Malformed posted ID cannot start a conversation");
            }
            for (var i = 0; i < 10; i++) Check(ConversationService.TryOpen(987654321), "Opening request within per-user budget " + i);
            Check(!ConversationService.TryOpen(987654321) && ConversationService.TryOpen(987654322), "Opening throttle rejects over-budget sender without blocking another sender");
            var accepted = 0;
            System.Threading.Tasks.Parallel.For(0, 40, i => { if (ConversationService.TryOpen(987654323)) System.Threading.Interlocked.Increment(ref accepted); });
            Check(accepted == 10, "Concurrent opening requests cannot exceed account budget");

            foreach (var path in args)
            {
                var host = new System.Web.Mvc.Razor.MvcWebPageRazorHost("~/Views/Messages/" + System.IO.Path.GetFileName(path), path);
                var engine = new System.Web.Razor.RazorTemplateEngine(host);
                using (var reader = System.IO.File.OpenText(path))
                {
                    var generated = engine.GenerateCode(reader);
                    Check(generated.ParserErrors.Count == 0, "Razor parses " + System.IO.Path.GetFileName(path) + ": " + string.Join("; ", generated.ParserErrors.Select(e => e.Message)));
                }
            }

            // Translation only: disabled initializer and ToString never execute or modify SQL.
            Database.SetInitializer<AppDbContext>(null);
            using (var db = new AppDbContext())
            {
                var sql = new ConversationService(db).Suggestions(actor).ToString();
                Check(sql.Contains("SELECT") && sql.Contains("FreelancerGroupMembers") && sql.Contains("ContractMembers") && sql.Contains("DirectConversations"), "Suggestion query translates through actual EF SQL provider without database execution");
            }
            Console.WriteLine("CONVERSATION_CHECKS=" + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
