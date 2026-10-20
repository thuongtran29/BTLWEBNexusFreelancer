using System;
using System.Data;
using System.Linq;
using NexusFreelance.Web.Data;

namespace NexusFreelance.Web.Services
{
    public static class ActivityAccessService
    {
        // The caller also requires a successful ViewResult and an authenticated active account.
        public static bool ShouldRecord(string method, bool isAjax, bool isChildAction, string controller, string action)
        {
            return ActivityAccessPolicy.ShouldRecord(method, isAjax, isChildAction, controller, action);
        }

        public static void RecordPageAccess(int userId, string controller, string action)
        {
            if (userId <= 0 || !ShouldRecord("GET", false, false, controller, action)) return;
            var now = DateTime.UtcNow;
            // Use an independent context: logging must never save pending business changes.
            using (var db = new AppDbContext())
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                // Serialize samples per account even when multiple tabs reach the same route together.
                if (db.Database.SqlQuery<int>("SELECT Id FROM dbo.Users WITH (UPDLOCK,HOLDLOCK) WHERE Id=@p0", userId).SingleOrDefault() == 0) return;
                var latest = db.Set<NexusFreelance.Web.Models.UserAccessLog>()
                    .Where(x => x.UserAccountId == userId && x.Controller == controller && x.Action == action)
                    .Select(x => (DateTime?)x.AccessedAtUtc).Max();
                if (!ActivityAccessPolicy.ShouldSample(latest, now)) return;
                db.Database.ExecuteSqlCommand("INSERT dbo.UserAccessLogs (UserAccountId, Controller, Action, AccessedAtUtc) VALUES (@p0,@p1,@p2,@p3)", userId, controller, action, now);
                // Hard per-account cap, including the current sample. Old accounts are cleaned in batches too.
                db.Database.ExecuteSqlCommand("DELETE FROM dbo.UserAccessLogs WHERE UserAccountId=@p0 AND Id NOT IN (SELECT TOP (500) Id FROM dbo.UserAccessLogs WHERE UserAccountId=@p0 ORDER BY AccessedAtUtc DESC, Id DESC)", userId);
                tx.Commit();
            }
            // Keep cross-account batch pruning outside the per-account lock transaction.
            using (var db = new AppDbContext())
                db.Database.ExecuteSqlCommand("DELETE TOP (1000) FROM dbo.UserAccessLogs WHERE AccessedAtUtc < @p0", now.AddDays(-30));
        }
    }
}
