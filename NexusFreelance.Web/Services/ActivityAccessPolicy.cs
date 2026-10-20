using System;
using System.Linq;

namespace NexusFreelance.Web.Services
{
    public static class ActivityAccessPolicy
    {
        public static bool ShouldSample(DateTime? latestUtc, DateTime nowUtc)
        {
            return !latestUtc.HasValue || nowUtc - latestUtc.Value >= TimeSpan.FromMinutes(15);
        }

        public static bool ShouldRecord(string method, bool isAjax, bool isChildAction, string controller, string action)
        {
            if (method != "GET" || isAjax || isChildAction || !RouteName(controller) || !RouteName(action)) return false;
            var routes = new[] { "Admin", "Projects", "Talent", "Profile", "ClientProfile", "Skills",
                "Workspace", "Dashboard", "Wallet", "Contracts", "Assessments", "GroupBids",
                "Groups", "Reports", "Proposals", "Messages", "Notifications", "SkillEvidence", "JobStudio", "Contribution" };
            var pages = new[] { "Index", "Details", "Edit", "Create", "Mine", "Users", "UserProfile", "Audit",
                "AuditDetails", "Activity", "Reports", "Certifications", "Badges", "Skills", "Questions",
                "History", "Admin", "Exam", "Result", "Manage", "EditPortfolio", "Inbox", "Settings", "Received", "Sent", "Thread", "New", "Freelancer", "Group", "ReviewDispute" };
            return routes.Contains(controller, StringComparer.OrdinalIgnoreCase) && pages.Contains(action, StringComparer.OrdinalIgnoreCase);
        }

        private static bool RouteName(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Length <= 60 && value.All(c => char.IsLetterOrDigit(c) || c == '_');
        }
    }
}
