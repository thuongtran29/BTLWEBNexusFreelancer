using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Services
{
    public sealed class GroupBidService
    {
        private readonly AppDbContext db;
        public GroupBidService(AppDbContext database) { db = database; }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private Project LockProject(int projectId)
        {
            var locked = db.Database.SqlQuery<int>(
                "SELECT [Id] FROM [dbo].[Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", projectId)
                .SingleOrDefault();
            Require(locked != 0, "Dự án không tồn tại.");
            return db.Projects.Single(p => p.Id == projectId);
        }

        public GroupBidPage CreatePage(int groupId, int projectId, int leaderId)
        {
            var group = db.FreelancerGroups.SingleOrDefault(g => g.Id == groupId);
            if (group == null || group.LeaderId != leaderId) return null;
            if (!db.FreelancerGroupMembers.Any(m => m.FreelancerGroupId == groupId &&
                m.UserAccountId == leaderId && m.Status == "Active")) return null;
            var project = db.Projects.Include(p => p.Requirements).SingleOrDefault(p => p.Id == projectId);
            if (project == null || project.Status != "Published" || project.ProjectType == "SOLO" || !RecruitmentPolicy.DateAllowsApplications(project.DeadlineUtc, DateTime.UtcNow)) return null;
            var members = db.FreelancerGroupMembers.Include(m => m.User)
                .Where(m => m.FreelancerGroupId == groupId && m.Status == "Active")
                .OrderBy(m => m.User.Name).ToList();
            return new GroupBidPage
            {
                Group = group, Project = project, Members = members,
                Form = new GroupBidForm
                {
                    FreelancerGroupId = groupId, ProjectId = projectId,
                    Candidates = members.Select(m => new GroupBidCandidateForm { UserAccountId = m.UserAccountId }).ToList()
                }
            };
        }

        public int Submit(int leaderId, GroupBidForm form)
        {
            Require(form != null && !string.IsNullOrWhiteSpace(form.Pitch) && form.Pitch.Trim().Length <= 2500,
                "Nội dung Proposal không hợp lệ.");
            var selected = (form.Candidates ?? new List<GroupBidCandidateForm>()).Where(x => x.Selected).ToList();
            Require(selected.Count >= 2 && selected.Count <= 50, "Nhóm cần từ 2 đến 50 thành viên được chọn.");
            Require(selected.Select(x => x.UserAccountId).Distinct().Count() == selected.Count,
                "Không thể gán một thành viên cho nhiều vị trí trong cùng Proposal nhóm.");
            Require(selected.All(x => WalletService.ValidAmount(x.QuotedPrice)),
                "Giá đề xuất không hợp lệ.");

            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                PaymentPlan.Parse(form.MilestonePlan, selected.Sum(x => x.QuotedPrice), form.ProjectId,form.DeliveryDays);
                var project = LockProject(form.ProjectId);
                Require(project.Status == "Published", "Dự án không còn tuyển.");
                Require(RecruitmentPolicy.DateAllowsApplications(project.DeadlineUtc, DateTime.UtcNow), "Dự án đã quá hạn dự kiến; cần khách hàng cập nhật trước khi gửi chào giá nhóm.");
                Require(project.ProjectType != "SOLO", "SOLO chỉ nhận chào giá cá nhân.");
                db.Database.SqlQuery<int>(
                    "SELECT [Id] FROM [dbo].[FreelancerGroups] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0",
                    form.FreelancerGroupId).SingleOrDefault();
                var group = db.FreelancerGroups.SingleOrDefault(g => g.Id == form.FreelancerGroupId);
                Require(group != null && group.LeaderId == leaderId, "Chỉ Leader nhóm được gửi Proposal.");
                var members = db.FreelancerGroupMembers.Where(m => m.FreelancerGroupId == group.Id && m.Status == "Active")
                    .Select(m => m.UserAccountId).ToList();
                Require(members.Count >= 2 && selected.Count==members.Count && selected.Any(x => x.UserAccountId == leaderId) && members.Contains(leaderId) && selected.All(x => members.Contains(x.UserAccountId)),
                    "Đội hình chứa thành viên chưa tham gia hoặc đã rời nhóm.");
                var candidates = selected.Select(x => x.UserAccountId).ToList();
                Require(db.Users.Count(u => candidates.Contains(u.Id) && u.Role == "Freelancer" && !u.IsSuspended) == selected.Count,
                    "Danh sách ứng viên không hợp lệ.");
                Require(!db.Users.Any(u => candidates.Contains(u.Id) && (u.Email == project.Client.Email || (project.Client.GoogleSubject != null && u.GoogleSubject == project.Client.GoogleSubject))), "Đội hình có hồ sơ cùng người với khách hàng; không thể tự giao dịch.");
                var roles = db.RoleRequirements.Where(r => r.ProjectId == project.Id).ToDictionary(r => r.Id);
                foreach (var portion in selected.GroupBy(x => x.RoleRequirementId))
                {
                    RoleRequirement role;
                    Require(roles.TryGetValue(portion.Key, out role), "Vai trò không thuộc dự án.");
                    // All competing Accept/Apply calls take the same project lock.
                    Require(db.RoleAssignments.Count(a => a.RoleRequirementId == role.Id) + portion.Count() <= role.RequiredCount,
                        "Vị trí không đủ chỗ trống cho đội hình.");
                }
                foreach (var candidate in selected)
                {
                    Require(!db.Proposals.Any(p => p.RoleRequirementId == candidate.RoleRequirementId &&
                        p.ApplicantId == candidate.UserAccountId), "Thành viên đã từng ứng tuyển vai trò này.");
                    Require(!db.RoleAssignments.Any(a => a.ProjectId == project.Id &&
                        a.FreelancerId == candidate.UserAccountId), "Thành viên đã được nhận vào dự án.");
                }

                var bid = new GroupBid
                {
                    ProjectId = project.Id, FreelancerGroupId = group.Id, LeaderId = leaderId,
                    MilestonePlan = form.MilestonePlan, DeliveryDays = form.DeliveryDays, Pitch = form.Pitch.Trim(), Status = "Pending", CreatedAtUtc = DateTime.UtcNow
                };
                db.GroupBids.Add(bid);
                db.SaveChanges();
                foreach (var candidate in selected)
                    db.Proposals.Add(new Proposal
                    {
                        GroupBidId = bid.Id, ProjectId = project.Id,
                        RoleRequirementId = candidate.RoleRequirementId, ApplicantId = candidate.UserAccountId,
                        MilestonePlan = bid.MilestonePlan, DeliveryDays = bid.DeliveryDays, Pitch = bid.Pitch, QuotedPrice = candidate.QuotedPrice, Status = "Pending",
                        CreatedAtUtc = bid.CreatedAtUtc
                    });
                db.SaveChanges();
                var proposals = db.Proposals.Where(p => p.GroupBidId == bid.Id).ToList();
                foreach (var proposal in proposals)
                {
                    var snapshot = new MatchingService(db).CreateSnapshot(proposal.ApplicantId, project,
                        roles[proposal.RoleRequirementId], proposal.QuotedPrice);
                    snapshot.ProposalId = proposal.Id;
                    db.MatchingResults.Add(snapshot);
                }
                db.ActivityLogs.Add(new ActivityLog { ProjectId = project.Id, ActorId = leaderId,
                    Action = "Submitted group bid #" + bid.Id, CreatedAtUtc = DateTime.UtcNow });
                NotificationService.Queue(db, project.ClientId, "Có Proposal nhóm mới cho dự án: " + project.Title,
                    "/Projects/Manage/" + project.Id);
                db.SaveChanges();
                tx.Commit();
                return bid.Id;
            }
        }

        public void Decide(int bidId, int clientId, bool accept)
        {
            var projectId = db.GroupBids.Where(b => b.Id == bidId).Select(b => (int?)b.ProjectId).SingleOrDefault();
            Require(projectId.HasValue, "Proposal nhóm không tồn tại.");
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var project = LockProject(projectId.Value);
                Require(project.ClientId == clientId, "Chỉ Client sở hữu dự án được xử lý Proposal.");
                Require(project.Status == "Published", "Dự án không còn tuyển.");
                var bid = db.GroupBids.Single(b => b.Id == bidId && b.ProjectId == project.Id);
                Require((bid.Status == "Pending" || bid.Status == "Shortlisted"), "Proposal nhóm đã được xử lý.");
                var proposals = db.Proposals.Where(p => p.GroupBidId == bidId).ToList();
                Require(proposals.Count > 0 && proposals.All(p => p.Status == "Pending" || p.Status == "Shortlisted"),
                    "Đội hình Proposal nhóm không hợp lệ.");

                if (accept)
                {
                    // Coordinate with Invite/Respond/ChangeMembership, which lock the group row.
                    // Lock order remains Project -> Group, matching Submit.
                    var lockedGroup = db.Database.SqlQuery<int>(
                        "SELECT [Id] FROM [dbo].[FreelancerGroups] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0",
                        bid.FreelancerGroupId).SingleOrDefault();
                    Require(lockedGroup != 0, "Nhóm không còn tồn tại.");
                    Require(db.FreelancerGroups.Any(g => g.Id == bid.FreelancerGroupId && g.LeaderId == bid.LeaderId),
                        "Leader nhóm đã thay đổi.");
                    // Proposal rows are the submitted roster snapshot. Later group
                    // membership changes do not rewrite a bid or an accepted contract.
                    var roster=proposals.Select(p=>p.ApplicantId).ToArray();
                    Require(db.Users.Count(u=>roster.Contains(u.Id) && u.Role=="Freelancer" && !u.IsSuspended)==roster.Length,
                        "Có tài khoản trong đội hình không còn đủ điều kiện nhận việc.");
                    Require(proposals.Select(p => p.ApplicantId).Distinct().Count() == proposals.Count,
                        "Ứng viên trùng nhau trong Proposal nhóm.");
                    foreach (var portion in proposals.GroupBy(p => p.RoleRequirementId).OrderBy(g => g.Key))
                    {
                        var lockedRole = db.Database.SqlQuery<int>(
                            "SELECT [Id] FROM [dbo].[RoleRequirements] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0",
                            portion.Key).SingleOrDefault();
                        Require(lockedRole != 0, "Vai trò không còn tồn tại.");
                        var role = db.RoleRequirements.Single(r => r.Id == portion.Key);
                        Require(role.ProjectId == project.Id, "Vai trò không thuộc dự án.");
                        Require(db.RoleAssignments.Count(a => a.RoleRequirementId == role.Id) + portion.Count() <= role.RequiredCount,
                            "Không còn đủ vị trí cho cả nhóm.");
                    }
                    var candidateIds = proposals.Select(p => p.ApplicantId).ToArray();
                    Require(!db.RoleAssignments.Any(a => a.ProjectId == project.Id && candidateIds.Contains(a.FreelancerId)),
                        "Một thành viên đã được nhận vào dự án.");

                    var team = db.ProjectTeams.SingleOrDefault(t => t.ProjectId == project.Id);
                    if (team == null)
                    {
                        team = new ProjectTeam { ProjectId = project.Id, CreatedAtUtc = DateTime.UtcNow };
                        db.ProjectTeams.Add(team);
                        db.SaveChanges();
                    }
                    var created = new List<RoleAssignment>();
                    foreach (var proposal in proposals)
                    {
                        proposal.Status = "Accepted";
                        var assignment = new RoleAssignment { ProjectId = project.Id, RoleRequirementId = proposal.RoleRequirementId,
                            FreelancerId = proposal.ApplicantId, ProposalId = proposal.Id, AssignedAtUtc = DateTime.UtcNow };
                        created.Add(assignment);
                        db.RoleAssignments.Add(assignment);
                        NotificationService.Queue(db, proposal.ApplicantId, "Proposal nhóm của bạn đã được chấp nhận",
                            "/Projects/Details/" + project.Id);
                    }
                    db.SaveChanges();
                    foreach (var assignment in created)
                        db.TeamMembers.Add(new TeamMember { ProjectTeamId = team.Id,
                            UserAccountId = assignment.FreelancerId, RoleAssignmentId = assignment.Id });
                    db.SaveChanges();
                    new ContractService(db).CreateSelected(project,proposals);
                }
                else
                {
                    foreach (var proposal in proposals)
                    {
                        proposal.Status = "Rejected";
                        NotificationService.Queue(db, proposal.ApplicantId, "Proposal nhóm của bạn đã bị từ chối",
                            "/Proposals/Mine");
                    }
                }
                bid.Status = accept ? "Accepted" : "Rejected";
                db.ActivityLogs.Add(new ActivityLog { ProjectId = project.Id, ActorId = clientId,
                    Action = (accept ? "Accepted" : "Rejected") + " group bid #" + bid.Id,
                    CreatedAtUtc = DateTime.UtcNow });
                db.SaveChanges();
                tx.Commit();
            }
        }

        public void Withdraw(int bidId, int leaderId)
        {
            var projectId = db.GroupBids.Where(b => b.Id == bidId).Select(b => (int?)b.ProjectId).SingleOrDefault();
            Require(projectId.HasValue, "Proposal nhóm không tồn tại.");
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var project = LockProject(projectId.Value);
                var bid = db.GroupBids.Single(b => b.Id == bidId);
                Require(bid.LeaderId == leaderId && db.FreelancerGroups.Any(g => g.Id == bid.FreelancerGroupId && g.LeaderId == leaderId),
                    "Chỉ Leader nhóm được rút Proposal nhóm.");
                Require((bid.Status == "Pending" || bid.Status == "Shortlisted"), "Chỉ có thể rút Proposal đang chờ xét.");
                var proposals = db.Proposals.Where(p => p.GroupBidId == bid.Id).ToList();
                Require(proposals.All(p => p.Status == "Pending" || p.Status == "Shortlisted"), "Proposal nhóm đã được xử lý.");
                bid.Status = "Withdrawn";
                foreach (var proposal in proposals) proposal.Status = "Withdrawn";
                db.ActivityLogs.Add(new ActivityLog { ProjectId = project.Id, ActorId = leaderId,
                    Action = "Withdrew group bid #" + bid.Id, CreatedAtUtc = DateTime.UtcNow });
                NotificationService.Queue(db, project.ClientId, "Một nhóm đã rút Proposal", "/Projects/Manage/" + project.Id);
                db.SaveChanges();
                tx.Commit();
            }
        }
    }
}
