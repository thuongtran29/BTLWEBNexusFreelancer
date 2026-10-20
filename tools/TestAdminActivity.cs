using System;
using NexusFreelance.Web.Services;

class TestAdminActivity
{
    static int failures;
    static void Check(bool condition, string label)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + label);
        if (!condition) failures++;
    }
    static int Main()
    {
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        Check(ActivityAccessPolicy.ShouldSample(null, now), "first function access is sampled");
        Check(!ActivityAccessPolicy.ShouldSample(now, now), "same instant is suppressed");
        Check(!ActivityAccessPolicy.ShouldSample(now.AddMinutes(-15).AddTicks(1), now), "within 15 minutes is suppressed");
        Check(ActivityAccessPolicy.ShouldSample(now.AddMinutes(-15), now), "15 minute boundary permits next sample");
        Check(ActivityAccessPolicy.ShouldSample(now.AddHours(-1), now), "later visit is sampled");
        Check(!ActivityAccessPolicy.ShouldSample(now.AddMinutes(1), now), "clock reversal cannot create extra sample");
        Check(ActivityAccessPolicy.ShouldRecord("GET", false, false, "Admin", "UserProfile"), "admin profile page is allowed");
        Check(ActivityAccessPolicy.ShouldRecord("GET", false, false, "Skills", "Exam"), "exam page is allowed");
        Check(ActivityAccessPolicy.ShouldRecord("GET", false, false, "Projects", "Manage"), "project management page is allowed");
        Check(ActivityAccessPolicy.ShouldRecord("GET", false, false, "profile", "index"), "route casing does not change policy");
        Check(!ActivityAccessPolicy.ShouldRecord("POST", false, false, "Projects", "Create"), "writes are excluded");
        Check(!ActivityAccessPolicy.ShouldRecord("GET", true, false, "Messages", "Index"), "AJAX polling is excluded");
        Check(!ActivityAccessPolicy.ShouldRecord("GET", false, true, "Admin", "Index"), "child actions are excluded");
        foreach (var controller in new[] { "Account", "AccountGoogle", "AccountRegistration", "Payments", "SubmissionApi", "ProfileImage", "Content", "WorkspaceFiles", "Unknown" })
            Check(!ActivityAccessPolicy.ShouldRecord("GET", false, false, controller, "Index"), controller + " excluded from access collection");
        foreach (var action in new[] { "Poll", "UnreadCount", "Download", "Callback", "Submit", "Index?token=private", "../Index", "Index/private", new string('x', 61), "" })
            Check(!ActivityAccessPolicy.ShouldRecord("GET", false, false, "Admin", action), "non-page or sensitive routing rejected: " + action);
        Check(!ActivityAccessPolicy.ShouldRecord("GET", false, false, null, "Index"), "missing controller is rejected");
        DateTime? start, end;
        Check(ActivityFilters.TryDates("2026-10-19", "2026-10-19", out start, out end) && start == now.Date && end == now.Date.AddDays(1) && start.Value.Kind == DateTimeKind.Utc, "same day filter includes whole UTC day");
        Check(ActivityFilters.TryDates("", "", out start, out end) && !start.HasValue && !end.HasValue, "empty dates are unbounded");
        Check(!ActivityFilters.TryDates("2026-10-20", "2026-10-19", out start, out end), "reversed dates rejected");
        Check(!ActivityFilters.TryDates("19/10/2026", "", out start, out end), "ambiguous culture date rejected");
        Check(!ActivityFilters.TryDates("2026-02-30", "", out start, out end), "invalid calendar date rejected");
        Check(!ActivityFilters.TryDates("", "9999-12-31", out start, out end), "exclusive upper bound overflow rejected");
        Check(ActivityFilters.SafeExternalUrl("https://example.com/profile"), "HTTPS public link allowed");
        foreach (var url in new[] { "javascript:alert(1)", "http://example.com", "https://user:secret@example.com", "/relative", "//example.com", "data:text/html,test", "" })
            Check(!ActivityFilters.SafeExternalUrl(url), "unsafe external link rejected: " + url);
        return failures == 0 ? 0 : 1;
    }
}
