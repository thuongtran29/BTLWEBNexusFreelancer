using System;
using System.Collections.Generic;

namespace NexusFreelance.Web.ViewModels
{
    // Only server-filtered records are ever sent to the logged-in user's dashboard.
    public class DashboardPage
    {
        public string Name { get; set; }
        public string Role { get; set; }
        public int Projects { get; set; }
        public int ActiveProjects { get; set; }
        public int PendingProposals { get; set; }
        public int OpenTasks { get; set; }
        public int UnreadNotifications { get; set; }
        public int Invitations { get; set; }
        public decimal TotalBudget { get; set; }
        public decimal WalletBalance { get; set; }
        public decimal WalletHeld { get; set; }
        public int DraftProjects { get; set; }
        public int PublishedProjects { get; set; }
        public int? NextDraftId { get; set; }
        public int ContractsToConfirm { get; set; }
        public int ContractsToFund { get; set; }
        public int ActiveContracts { get; set; }
        public int ContractsToReview { get; set; }
        public int DisputedContracts { get; set; }
        public bool HasProfileIntroduction { get; set; }
        public int PortfolioItems { get; set; }
        public List<DashboardProjectRow> RecentProjects { get; set; } = new List<DashboardProjectRow>();
        public List<DashboardTaskRow> MyTasks { get; set; } = new List<DashboardTaskRow>();
    }

    public class DashboardProjectRow
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Status { get; set; }
        public decimal Budget { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public int? WorkspaceId { get; set; }
    }

    public class DashboardTaskRow
    {
        public string Title { get; set; }
        public string Status { get; set; }
        public string Priority { get; set; }
        public DateTime? DueAtUtc { get; set; }
        public int WorkspaceId { get; set; }
    }
}
