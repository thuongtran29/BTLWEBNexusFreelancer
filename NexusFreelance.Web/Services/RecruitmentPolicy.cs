using System;

namespace NexusFreelance.Web.Services
{
    public static class RecruitmentPolicy
    {
        // Project dates are calendar days stored as UTC midnight; include the full final day.
        public static bool DateAllowsApplications(DateTime? deadlineUtc, DateTime nowUtc) =>
            !deadlineUtc.HasValue || deadlineUtc.Value.Date >= nowUtc.Date;
    }
}
