using System;

namespace NexusFreelance.Web.Services
{
    public static class ContributionScoring
    {
        // v1 counts verified Done tasks and approved latest deliverables plus Client rating.
        // Cap prevents arbitrarily many small tasks from increasing XP indefinitely.
        public static int Calculate(int doneTasks, int approvedDeliverables, int clientRating)
        {
            if (doneTasks < 0 || approvedDeliverables < 0 || clientRating < 1 || clientRating > 5)
                throw new ArgumentOutOfRangeException("Evidence counts must be nonnegative and rating between 1 and 5.");
            return (int)Math.Min(100L, (long)doneTasks * 10 + (long)approvedDeliverables * 20 + clientRating * 10L);
        }
    }
}
