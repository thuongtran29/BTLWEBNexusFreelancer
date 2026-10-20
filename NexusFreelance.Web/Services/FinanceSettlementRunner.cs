using System;
using System.Data;
using System.Linq;
using System.Threading;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Services
{
    // Overdue review sends a reminder, never a financial instruction or acceptance.
    // The project lock serializes reminders with client approval and disputes.
    public static class FinanceSettlementRunner
    {
        private static Timer timer;
        private static int busy;
        public static void Start()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("NEXUS_REMINDERS_EXTERNAL"), "true", StringComparison.OrdinalIgnoreCase))
                timer = new Timer(_ => Tick(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }
        public static void Stop() { timer?.Dispose(); }
        public static void Tick()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                RunOnce();
            }
            catch (Exception)
            { System.Diagnostics.Trace.TraceWarning("Scheduled review could not complete; pending rows will be retried."); }
            finally { Interlocked.Exchange(ref busy, 0); }
        }
        // A SQL session lock coordinates IIS workers and the independent scheduled worker.
        // Due rows and reminder receipts remain in SQL, so a restart resumes pending work.
        public static bool RunOnce()
        {
            using (var db = new AppDbContext())
            {
                db.Database.Connection.Open();
                var acquired = db.Database.SqlQuery<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'NexusReviewReminders.v1', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @r;").Single();
                if (acquired < 0) return false;
                try
                {
                    new SkillEvidenceService(db).Expire();
                    var cutoff = DateTime.UtcNow.AddDays(-7);
                    var ids = db.EscrowMilestones.Where(m => m.Status == "Submitted" && m.SubmittedAtUtc <= cutoff && !m.ReviewRemindedAtUtc.HasValue &&
                        (m.ContractId.HasValue ? m.Contract.Status == "Active" : m.Project.Status == "InProgress") &&
                        !db.EscrowDisputes.Any(d => d.ProjectId == m.ProjectId && d.ContractId == m.ContractId && d.Status == "Pending") &&
                        db.Deliverables.Any(d => d.EscrowMilestoneId == m.Id && d.Status == "Submitted" && !db.Deliverables.Any(next => next.RevisesId == d.Id)))
                        .OrderBy(m => m.Id).Select(m => m.Id).Take(20).ToArray();
                    foreach (var id in ids) RemindOverdue(id);
                    return true;
                }
                finally { db.Database.ExecuteSqlCommand("EXEC sys.sp_releaseapplock @Resource=N'NexusReviewReminders.v1', @LockOwner='Session';"); }
            }
        }
        public static void RemindOverdue(int id)
        {
            using (var db = new AppDbContext())
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var projectId = db.EscrowMilestones.Where(m => m.Id == id).Select(m => (int?)m.ProjectId).SingleOrDefault();
                if (!projectId.HasValue) return;
                var project = db.Projects.SqlQuery("SELECT * FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", projectId.Value).Single();
                var milestone = db.EscrowMilestones.Single(m => m.Id == id);
                if ((milestone.ContractId.HasValue ? !db.Contracts.Any(c=>c.Id==milestone.ContractId && c.Status=="Active") : project.Status != "InProgress") || milestone.Status != "Submitted" || milestone.ReviewRemindedAtUtc.HasValue || !milestone.SubmittedAtUtc.HasValue || milestone.SubmittedAtUtc.Value > DateTime.UtcNow.AddDays(-7)) return;
                if (db.EscrowDisputes.Any(d => d.ProjectId == project.Id && d.ContractId==milestone.ContractId && d.Status == "Pending")) return;
                var delivery = db.Deliverables.Where(d => d.EscrowMilestoneId == id && d.Status == "Submitted" && !db.Deliverables.Any(next => next.RevisesId == d.Id)).OrderByDescending(d => d.Id).FirstOrDefault();
                if (delivery == null) return;
                milestone.ReviewRemindedAtUtc = DateTime.UtcNow;
                db.MilestoneReviewReminders.Add(new MilestoneReviewReminder { EscrowMilestoneId = id, DeliverableId = delivery.Id, CreatedAtUtc = milestone.ReviewRemindedAtUtc.Value });
                NotificationService.Queue(db, project.ClientId, "Mốc #" + id + ": vui lòng nghiệm thu bàn giao đã chờ 7 ngày", "/Wallet/Project/" + project.Id);
                NotificationService.QueueAdmins(db, "Mốc #" + id + " đang chờ nghiệm thu quá 7 ngày; cần hỗ trợ các bên", "/Wallet/Project/" + project.Id);
                db.SaveChanges(); tx.Commit();
            }
        }
    }
}
