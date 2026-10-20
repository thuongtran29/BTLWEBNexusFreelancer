using System;
using System.Data.Entity;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Services;

// Runs without IIS/HTTP. Never migrates, accepts a deliverable, or moves money.
internal static class ReminderWorker
{
    public static int Main()
    {
        Database.SetInitializer<AppDbContext>(null);
        try
        {
            var executed = FinanceSettlementRunner.RunOnce();
            Console.WriteLine("{\"atUtc\":\"" + DateTime.UtcNow.ToString("o") + "\",\"job\":\"review-reminders\",\"status\":\"" +
                (executed ? "completed" : "skipped-busy") + "\"}");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine("{\"atUtc\":\"" + DateTime.UtcNow.ToString("o") + "\",\"job\":\"review-reminders\",\"status\":\"failed\",\"errorType\":\"" + error.GetType().Name + "\"}");
            return 1;
        }
    }
}
