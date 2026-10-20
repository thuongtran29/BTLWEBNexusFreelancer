using System.Collections.Generic;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Services
{
    // Read-only work queues share the same visibility boundary as the contract list.
    public static class ContractInbox
    {
        public static readonly KeyValuePair<string,string>[] Stages = {
            new KeyValuePair<string,string>("", "Tất cả"),
            new KeyValuePair<string,string>("confirm", "Cần xác nhận"),
            new KeyValuePair<string,string>("fund", "Chờ ký quỹ"),
            new KeyValuePair<string,string>("active", "Đang thực hiện"),
            new KeyValuePair<string,string>("review", "Chờ nghiệm thu"),
            new KeyValuePair<string,string>("issues", "Tranh chấp"),
            new KeyValuePair<string,string>("closed", "Đã kết thúc")
        };
        public static IQueryable<ProjectContract> VisibleTo(AppDbContext db, int userId, string role)
        {
            var query = db.Contracts.AsQueryable();
            return role == "Admin" ? query : query.Where(c => c.Project.ClientId == userId || c.CounterpartyId == userId ||
                db.ContractMembers.Any(m => m.ContractId == c.Id && m.UserAccountId == userId));
        }
        public static IQueryable<ProjectContract> Filter(AppDbContext db, IQueryable<ProjectContract> query, string stage, int userId, bool admin)
        {
            switch (stage)
            {
                case "confirm": return query.Where(c => c.Status == "AwaitingSignatures" && (admin || !db.ContractAcceptances.Any(a =>
                    a.UserAccountId == userId && a.Version.ContractId == c.Id && a.Version.VersionNumber == c.CurrentVersion && a.AcceptedHash == a.Version.Hash)));
                case "fund": return query.Where(c => c.Status == "AwaitingEscrow");
                case "active": return query.Where(c => c.Status == "Active");
                case "review": return query.Where(c => c.Status == "Active" && db.EscrowMilestones.Any(m => m.ContractId == c.Id && m.Status == "Submitted"));
                case "issues": return query.Where(c => c.Status == "Disputed");
                case "closed": return query.Where(c => c.Status == "Completed" || c.Status == "Terminated" || c.Status == "Declined");
                default: return query;
            }
        }
    }
}
