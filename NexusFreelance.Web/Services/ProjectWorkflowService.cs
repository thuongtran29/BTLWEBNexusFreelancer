using System;
using System.Data;
using System.Data.Entity;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Services
{
    public sealed class ProjectWorkflowService
    {
        private readonly AppDbContext db;
        public ProjectWorkflowService(AppDbContext database) { db = database; }

        private static void Must(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private void Log(int projectId, int actorId, string action)
        {
            db.ActivityLogs.Add(new ActivityLog { ProjectId = projectId, ActorId = actorId, Action = action, CreatedAtUtc = DateTime.UtcNow });
        }

        // All project lifecycle mutations lock the same row as Apply, Accept and LockTeam.
        // Keeping the transition and its audit entry in one transaction prevents stale writes.
        public void ChangeProjectStatus(int projectId, int clientId, string action)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var locked = db.Database.SqlQuery<int>(
                    "SELECT [Id] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", projectId).SingleOrDefault();
                Must(locked != 0, "Dự án không tồn tại.");
                var project = db.Projects.Single(p => p.Id == projectId);
                Must(project.ClientId == clientId, "Bạn không có quyền quản lý dự án.");

                string next;
                switch (action)
                {
                    case "Pause":
                        Must(project.Status == "Published", "Chỉ tạm dừng dự án đang tuyển.");
                        next = "Paused";
                        break;
                    case "Resume":
                        Must(project.Status == "Paused", "Chỉ mở lại dự án đang tạm dừng.");
                        Must(RecruitmentPolicy.DateAllowsApplications(project.DeadlineUtc, DateTime.UtcNow), "Cập nhật hạn dự kiến trước khi tiếp tục tuyển.");
                        next = "Published";
                        break;
                    case "Cancel":
                        Must(project.Status == "Draft" || project.Status == "Published" || project.Status == "Paused",
                            "Chỉ hủy dự án chưa mở Workspace.");
                        Must(!db.Contracts.Any(c=>c.ProjectId==projectId && c.Status!="Declined"),"Cần quyết toán hợp đồng trước khi hủy dự án.");
                        Must(!db.RoleAssignments.Any(a=>a.ProjectId==projectId), "Còn vị trí đã nhận; cần kết thúc hợp đồng trước khi hủy dự án.");
                        var waiting = db.Proposals.Where(p => p.ProjectId == projectId && (p.Status == "Pending" || p.Status == "Shortlisted")).ToList();
                        foreach (var proposal in waiting) proposal.Status = "Rejected";
                        foreach (var bid in db.GroupBids.Where(b => b.ProjectId == projectId && (b.Status == "Pending" || b.Status == "Shortlisted"))) bid.Status = "Rejected";
                        foreach (var recipient in waiting.Select(p => p.ApplicantId).Distinct())
                            NotificationService.Queue(db, recipient, "Dự án đã hủy: " + project.Title.Substring(0, Math.Min(150, project.Title.Length)), "/Proposals/Mine", clientId, projectId);
                        next = "Cancelled";
                        break;
                    default:
                        throw new InvalidOperationException("Thao tác không hợp lệ.");
                }
                project.Status = next;
                Log(projectId, clientId, action + " project");
                db.SaveChanges();
                tx.Commit();
            }
        }

        public void Shortlist(int projectId, int proposalId, int clientId)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                db.Database.SqlQuery<int>("SELECT Id FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", projectId).Single();
                var project = db.Projects.Single(p => p.Id == projectId);
                Must(project.ClientId == clientId && project.Status == "Published", "Chỉ khách hàng được chọn sơ bộ khi đang tuyển.");
                var proposal = db.Proposals.SingleOrDefault(p => p.Id == proposalId && p.ProjectId == projectId);
                Must(proposal != null && proposal.Status == "Pending", "Chào giá không còn chờ xét.");
                if (proposal.GroupBidId.HasValue)
                {
                    var bid = db.GroupBids.Single(b => b.Id == proposal.GroupBidId.Value);
                    Must(bid.Status == "Pending", "Chào giá nhóm đã xử lý."); bid.Status = "Shortlisted";
                    foreach (var p in db.Proposals.Where(p => p.GroupBidId == bid.Id)) p.Status = "Shortlisted";
                    NotificationService.Queue(db, bid.LeaderId, "Nhóm được chọn sơ bộ", "/Groups/Details/" + bid.FreelancerGroupId);
                }
                else { proposal.Status = "Shortlisted"; NotificationService.Queue(db, proposal.ApplicantId, "Bạn được chọn sơ bộ", "/Proposals/Mine"); }
                Log(projectId, clientId, "Shortlisted proposal #" + proposalId); db.SaveChanges(); tx.Commit();
            }
        }

        public void AcceptProposal(int projectId, int proposalId, int clientId)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                // Coordinate with LockTeam/CompleteProject on the same project row.
                db.Database.SqlQuery<int>("SELECT [Id] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", projectId).Single();
                var project = db.Projects.SingleOrDefault(p => p.Id == projectId);
                Must(project != null && project.ClientId == clientId, "Bạn không có quyền quản lý dự án.");
                Must(project.Status == "Published", "Dự án không còn tuyển thành viên.");
                Must(project.ProjectType != "TEAM", "Dự án nhóm chỉ nhận chào giá theo đội hình.");
                var proposal = db.Proposals.SingleOrDefault(p => p.Id == proposalId && p.ProjectId == projectId);
                Must(proposal != null && (proposal.Status == "Pending" || proposal.Status == "Shortlisted"), "Proposal không hợp lệ hoặc đã xử lý.");
                Must(!proposal.GroupBidId.HasValue, "Proposal nhóm phải được xử lý toàn bộ qua Team Builder nhóm.");

                // SQL Server update lock serializes accept operations competing for the last opening.
                db.Database.SqlQuery<int>("SELECT [Id] FROM [dbo].[RoleRequirements] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", proposal.RoleRequirementId).Single();
                var requirement = db.RoleRequirements.Single(r => r.Id == proposal.RoleRequirementId);
                Must(requirement.ProjectId == projectId, "Vị trí không thuộc dự án.");
                var count = db.RoleAssignments.Count(a => a.RoleRequirementId == requirement.Id);
                Must(count < requirement.RequiredCount, "Vị trí đã tuyển đủ.");
                Must(!db.RoleAssignments.Any(a => a.ProjectId == projectId && a.FreelancerId == proposal.ApplicantId), "Ứng viên đã được nhận vào dự án.");

                proposal.Status = "Accepted";
                var assignment = new RoleAssignment
                {
                    ProjectId = projectId, RoleRequirementId = requirement.Id, ProposalId = proposal.Id,
                    FreelancerId = proposal.ApplicantId, AssignedAtUtc = DateTime.UtcNow
                };
                db.RoleAssignments.Add(assignment);
                var team = db.ProjectTeams.SingleOrDefault(t => t.ProjectId == projectId);
                if (team == null)
                {
                    team = new ProjectTeam { ProjectId = projectId, CreatedAtUtc = DateTime.UtcNow };
                    db.ProjectTeams.Add(team);
                }
                db.SaveChanges();
                db.TeamMembers.Add(new TeamMember { ProjectTeamId = team.Id, UserAccountId = proposal.ApplicantId, RoleAssignmentId = assignment.Id });
                new ContractService(db).CreateSelected(project,new[]{proposal});
                Log(projectId, clientId, "Accepted proposal #" + proposal.Id + " for role #" + requirement.Id);
                NotificationService.Queue(db, proposal.ApplicantId, "Proposal của bạn đã được chấp nhận", "/Projects/Details/" + projectId);
                db.SaveChanges();
                tx.Commit();
            }
        }

        public void RejectProposal(int projectId, int proposalId, int clientId)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                // Acquire the project lock first, matching Accept and Withdraw.
                var lockedProject = db.Database.SqlQuery<int>(
                    "SELECT [Id] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", projectId).SingleOrDefault();
                Must(lockedProject != 0, "Dự án không tồn tại.");
                var project = db.Projects.Single(p => p.Id == projectId);
                Must(project.ClientId == clientId, "Bạn không có quyền quản lý dự án.");
                Must(project.Status == "Published", "Dự án không còn tuyển thành viên.");
                var lockedProposal = db.Database.SqlQuery<int>(
                    "SELECT [Id] FROM [dbo].[Proposals] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0 AND [ProjectId] = @p1",
                    proposalId, projectId).SingleOrDefault();
                Must(lockedProposal != 0, "Proposal không tồn tại trong dự án.");
                var proposal = db.Proposals.Single(p => p.Id == proposalId && p.ProjectId == projectId);
                Must((proposal.Status == "Pending" || proposal.Status == "Shortlisted"), "Proposal không hợp lệ hoặc đã xử lý.");
                Must(!proposal.GroupBidId.HasValue, "Proposal nhóm phải được xử lý toàn bộ qua Team Builder nhóm.");
                Must(!db.RoleAssignments.Any(a => a.ProposalId == proposalId), "Proposal đã được phân vai.");
                proposal.Status = "Rejected";
                Log(projectId, clientId, "Rejected proposal #" + proposalId);
                NotificationService.Queue(db, proposal.ApplicantId, "Proposal của bạn đã bị từ chối", "/Proposals/Mine");
                db.SaveChanges();
                tx.Commit();
            }
        }

        public void LockTeamAndCreateWorkspace(int projectId, int clientId)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                db.Database.SqlQuery<int>("SELECT [Id] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", projectId).Single();
                var project = db.Projects.SingleOrDefault(p => p.Id == projectId);
                Must(project != null && project.ClientId == clientId, "Bạn không có quyền quản lý dự án.");
                Must(project.Status == "Published", "Dự án không ở trạng thái tuyển dụng.");
                var roles = db.RoleRequirements.Where(r => r.ProjectId == projectId).ToList();
                Must(roles.Count > 0, "Chưa khai báo vị trí tuyển.");
                foreach (var role in roles)
                    Must(db.RoleAssignments.Count(a => a.RoleRequirementId == role.Id) == role.RequiredCount,
                        "Phải tuyển đủ số người cho mọi vị trí trước khi khóa đội.");

                var team = db.ProjectTeams.SingleOrDefault(t => t.ProjectId == projectId);
                Must(team != null && !team.Locked, "Đội chưa tồn tại hoặc đã bị khóa.");

                team.Locked = true;
                project.Status = "AwaitingEscrow";
                new WalletService(db).CreateContract(project);
                new WalletService(db).RefreshProjectState(project);
                Log(projectId, clientId, "Locked team and opened workspace");
                db.SaveChanges();
                tx.Commit();
            }
        }

        public void CompleteProject(int projectId, int clientId)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                db.Database.SqlQuery<int>("SELECT [Id] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", projectId).Single();
                var project = db.Projects.SingleOrDefault(p => p.Id == projectId);
                Must(project != null && project.ClientId == clientId, "Không có quyền quản lý dự án.");
                Must(project.Status == "InProgress", "Dự án không đang thực hiện.");
                Must(!db.EscrowMilestones.Any(m => m.ProjectId == projectId && m.Status != "Released"), "Còn mốc chưa được giải ngân.");
                Must(!db.Contracts.Any(c=>c.ProjectId==projectId && c.Status!="Completed" && c.Status!="Terminated" && c.Status!="Declined"),"Còn hợp đồng chưa quyết toán.");
                Must(!db.EscrowDisputes.Any(d=>d.ProjectId==projectId && d.Status=="Pending"),"Còn khiếu nại chưa xử lý.");
                Must(db.Workspaces.Any(w=>w.ProjectId==projectId),"Workspace chưa tồn tại.");
                Must(!db.Deliverables.Any(d=>d.Workspace.ProjectId==projectId && d.Status!="Approved" && !db.Deliverables.Any(next=>next.RevisesId==d.Id)),"Còn bàn giao chưa nghiệm thu.");
                project.Status = "Completed";
                Log(projectId, clientId, "Completed project");
                db.SaveChanges();
                tx.Commit();
            }
        }

        public void AddReview(int projectId, int freelancerId, int clientId, int rating, string comment)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                db.Database.SqlQuery<int>("SELECT [Id] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", projectId).Single();
                var project = db.Projects.SingleOrDefault(p => p.Id == projectId);
                Must(project != null && project.ClientId == clientId && project.Status == "Completed", "Chỉ Client được đánh giá sau khi hoàn tất.");
                Must(freelancerId != clientId, "Không được tự đánh giá chính mình.");
                Must(rating >= 1 && rating <= 5, "Điểm đánh giá phải từ 1 đến 5.");
                Must(db.RoleAssignments.Any(a => a.ProjectId == projectId && a.FreelancerId == freelancerId), "Người này không tham gia dự án.");
                Must(!db.Reviews.Any(r => r.ProjectId == projectId && r.FreelancerId == freelancerId), "Đã đánh giá thành viên này.");
                var reviewedAt = DateTime.UtcNow;
                var review = new Review { ProjectId = projectId, ClientId = clientId, FreelancerId = freelancerId,
                    Rating = rating, Comment = comment, CreatedAtUtc = reviewedAt };
                db.Reviews.Add(review);

                // Snapshot only evidence associated with this member and this project.
                // Old rejected deliverable versions are excluded from the current count.
                var done = db.Tasks.Count(t => t.Workspace.ProjectId == projectId &&
                    t.AssigneeId == freelancerId && t.Status == "Done");
                var approved = db.Deliverables.Count(d => d.Workspace.ProjectId == projectId &&
                    d.SubmittedById == freelancerId && d.Status == "Approved" &&
                    !db.Deliverables.Any(next => next.RevisesId == d.Id));
                var score = ContributionScoring.Calculate(done, approved, rating);
                var role = db.RoleAssignments.Where(a => a.ProjectId == projectId && a.FreelancerId == freelancerId)
                    .Select(a => a.Requirement.Title).FirstOrDefault() ?? "Không xác định";
                var contribution = new ContributionRecord { ProjectId = projectId, UserAccountId = freelancerId,
                    Review = review, DoneTasks = done, ApprovedDeliverables = approved,
                    ClientRating = rating, EvidenceScore = score, Method = "ReviewSnapshot-v1", CreatedAtUtc = reviewedAt };
                db.ContributionRecords.Add(contribution);
                db.XPTransactions.Add(new XPTransaction { UserAccountId = freelancerId, Contribution = contribution,
                    Points = score, Reason = "Completed project review (v1)", CreatedAtUtc = reviewedAt });
                db.SkillDNAHistories.Add(new SkillDNAHistory { UserAccountId = freelancerId, Contribution = contribution,
                    RoleTitle = role, EvidenceScore = score, CreatedAtUtc = reviewedAt });
                Log(projectId, clientId, "Reviewed freelancer #" + freelancerId);
                db.SaveChanges();
                tx.Commit();
            }
        }
    }
}
