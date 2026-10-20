using System;
using System.Data;
using System.Data.Entity;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Services
{
    public class FreelancerGroupService
    {
        private readonly AppDbContext db;
        public FreelancerGroupService(AppDbContext context) { db = context; }

        public GroupIndexPage LoadIndex(int userId)
        {
            return new GroupIndexPage
            {
                Memberships = db.FreelancerGroupMembers.Include(m => m.Group).Include(m => m.Group.Leader)
                    .Where(m => m.UserAccountId == userId && m.Status == "Active")
                    .OrderByDescending(m => m.Group.CreatedAtUtc).ToList(),
                Invitations = db.FreelancerGroupMembers.Include(m => m.Group).Include(m => m.Group.Leader)
                    .Where(m => m.UserAccountId == userId && m.Status == "Invited")
                    .OrderByDescending(m => m.InvitedAtUtc).ToList()
            };
        }

        public GroupDetailsPage LoadDetails(int groupId, int userId)
        {
            var group = db.FreelancerGroups.Include(g => g.Leader).SingleOrDefault(g => g.Id == groupId);
            if (group == null || !db.FreelancerGroupMembers.Any(m => m.FreelancerGroupId == groupId &&
                    m.UserAccountId == userId && m.Status == "Active")) return null;
            return new GroupDetailsPage
            {
                Group = group,
                IsLeader = group.LeaderId == userId,
                IsLeaderVisibleInDirectory = !db.FreelancerProfiles.Any(p =>
                    p.UserAccountId == group.LeaderId && p.IsPublic == false),
                Members = db.FreelancerGroupMembers.Include(m => m.User)
                    .Where(m => m.FreelancerGroupId == groupId && (m.Status == "Active" || m.Status == "Invited"))
                    .OrderBy(m => m.User.Name).ToList(),
                Events = db.FreelancerGroupEvents.Include(e => e.Actor)
                    .Where(e => e.FreelancerGroupId == groupId).OrderByDescending(e => e.Id).Take(30).ToList()
            };
        }

        // A current group member can see only projects where they are also an assigned
        // team member. A group invitation or membership alone grants no workspace access.
        public GroupDashboardData LoadDashboard(int groupId, int userId)
        {
            if (!db.FreelancerGroupMembers.Any(m => m.FreelancerGroupId == groupId &&
                m.UserAccountId == userId && m.Status == "Active"))
                throw new UnauthorizedAccessException();

            var visibleMemberIds = db.FreelancerGroupMembers.AsNoTracking()
                .Where(m => m.FreelancerGroupId == groupId && (m.Status == "Active" || m.Status == "Invited"))
                .Select(m => m.UserAccountId).ToArray();
            var roles = db.FreelancerProfiles.AsNoTracking()
                .Where(p => visibleMemberIds.Contains(p.UserAccountId))
                .Select(p => new { p.UserAccountId, p.CoreRole })
                .ToList().ToDictionary(p => p.UserAccountId, p => p.CoreRole);

            var projects = db.Workspaces.AsNoTracking()
                .Where(w => w.ContractId.HasValue ?
                    db.Contracts.Any(c=>c.Id==w.ContractId && c.GroupBid.FreelancerGroupId==groupId && c.Status!="Declined") &&
                    db.ContractMembers.Any(m=>m.ContractId==w.ContractId && m.UserAccountId==userId) :
                    db.GroupBids.Any(b => b.FreelancerGroupId == groupId &&
                    b.ProjectId == w.ProjectId && b.Status == "Accepted") &&
                    db.TeamMembers.Any(m => m.ProjectTeamId == w.ProjectTeamId &&
                        m.UserAccountId == userId))
                .OrderByDescending(w => w.CreatedAtUtc).Take(20)
                .Select(w => new GroupProjectProgress
                {
                    ProjectId = w.ProjectId, WorkspaceId = w.Id, ContractId=w.ContractId,
                    ProjectTitle = w.Project.Title, ProjectStatus = w.Project.Status
                }).ToList();
            if (projects.Count == 0) return new GroupDashboardData { MemberRoles = roles };

            var workspaceIds = projects.Select(p => p.WorkspaceId).ToArray();
            var projectIds = projects.Select(p => p.ProjectId).ToArray();
            var taskCounts = db.Tasks.AsNoTracking()
                .Where(t => workspaceIds.Contains(t.WorkspaceId))
                .GroupBy(t => t.WorkspaceId)
                .Select(g => new { WorkspaceId = g.Key, Total = g.Count(), Done = g.Count(t => t.Status == "Done") })
                .ToList().ToDictionary(x => x.WorkspaceId);

            var currentMemberIds = db.FreelancerGroupMembers.AsNoTracking()
                .Where(m => m.FreelancerGroupId == groupId && m.Status == "Active")
                .Select(m => m.UserAccountId).ToArray();
            var evidence = db.ContributionRecords.AsNoTracking()
                .Where(c => projectIds.Contains(c.ProjectId) && currentMemberIds.Contains(c.UserAccountId) &&
                    db.Workspaces.Any(w => workspaceIds.Contains(w.Id) && w.ProjectId == c.ProjectId &&
                        (w.ContractId.HasValue ? db.ContractMembers.Any(m=>m.ContractId==w.ContractId && m.UserAccountId==c.UserAccountId) :
                        db.TeamMembers.Any(m => m.ProjectTeamId == w.ProjectTeamId &&
                            m.UserAccountId == c.UserAccountId))))
                .Select(c => new { c.ProjectId, c.UserAccountId, c.EvidenceScore })
                .ToList().GroupBy(c => c.ProjectId)
                .ToDictionary(g => g.Key, g => new
                {
                    Score = g.Sum(c => c.EvidenceScore),
                    Contributors = g.Select(c => c.UserAccountId).Distinct().Count()
                });
            var memoryIds = db.ProjectMemories.AsNoTracking()
                .Where(m => projectIds.Contains(m.ProjectId))
                .Select(m => m.ProjectId).Distinct().ToArray();

            foreach (var project in projects)
            {
                if (taskCounts.ContainsKey(project.WorkspaceId))
                {
                    project.TaskCount = taskCounts[project.WorkspaceId].Total;
                    project.DoneTaskCount = taskCounts[project.WorkspaceId].Done;
                }
                if (evidence.ContainsKey(project.ProjectId))
                {
                    project.ContributorCount = evidence[project.ProjectId].Contributors;
                    project.EvidenceScoreTotal = evidence[project.ProjectId].Score;
                }
                project.HasProjectMemory = !project.ContractId.HasValue && memoryIds.Contains(project.ProjectId);
            }
            return new GroupDashboardData { Projects = projects, MemberRoles = roles };
        }

        public int Create(int leaderId, CreateGroupForm form)
        {
            var group = new FreelancerGroup
            {
                LeaderId = leaderId, Name = form.Name.Trim(), Description = form.Description.Trim(),
                IsPublic = form.IsPublic, Industry = IndustryCatalog.Normalize(form.Industry), WorkStyle = form.WorkStyle ?? "Remote",
                Specialties = form.Specialties?.Trim(), Collaboration = form.Collaboration?.Trim(), TargetSize = form.TargetSize, MinimumBudget = form.MinimumBudget,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.FreelancerGroups.Add(group);
            db.FreelancerGroupMembers.Add(new FreelancerGroupMember
            {
                Group = group, UserAccountId = leaderId, Status = "Active",
                InvitedAtUtc = group.CreatedAtUtc, RespondedAtUtc = group.CreatedAtUtc
            });
            db.FreelancerGroupEvents.Add(new FreelancerGroupEvent
            {
                Group = group, ActorId = leaderId, Description = "Created freelancer group", CreatedAtUtc = group.CreatedAtUtc
            });
            db.SaveChanges();
            return group.Id;
        }

        public void UpdateVisibility(int groupId, int leaderId, bool isPublic)
        {
            var group = db.FreelancerGroups.SingleOrDefault(g => g.Id == groupId);
            if (group == null) throw new InvalidOperationException("Nhóm không tồn tại.");
            if (group.LeaderId != leaderId) throw new UnauthorizedAccessException();
            group.IsPublic = isPublic;
            db.SaveChanges();
        }

        // Serialize group membership mutations on a single row to prevent duplicate/racing invites.
        private FreelancerGroup LockGroup(int groupId)
        {
            db.Database.SqlQuery<int>("SELECT [Id] FROM [dbo].[FreelancerGroups] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", groupId)
                .SingleOrDefault();
            var group = db.FreelancerGroups.SingleOrDefault(g => g.Id == groupId);
            if (group == null) throw new InvalidOperationException("Nhóm không tồn tại.");
            return group;
        }

        private void Record(int groupId, int actor, string action)
        {
            db.FreelancerGroupEvents.Add(new FreelancerGroupEvent
            {
                FreelancerGroupId = groupId, ActorId = actor,
                Description = action, CreatedAtUtc = DateTime.UtcNow
            });
        }

        public void Invite(int groupId, int leaderId, string email)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            { InviteUnderLock(groupId, leaderId, email); tx.Commit(); }
        }
        internal void InviteUnderLock(int groupId, int leaderId, string email)
        {
            if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Invitation requires a transaction.");
        var group = LockGroup(groupId);
        if (group.LeaderId != leaderId) throw new UnauthorizedAccessException();
        var target = db.Users.SingleOrDefault(u => u.Email == email && u.Role == "Freelancer");
        if (target == null || target.Role != "Freelancer" || target.Id == leaderId)
            throw new InvalidOperationException("Chỉ có thể mời tài khoản Freelancer khác đã đăng ký.");
        var member = db.FreelancerGroupMembers.SingleOrDefault(m =>
            m.FreelancerGroupId == groupId && m.UserAccountId == target.Id);
        if (member != null && (member.Status == "Invited" || member.Status == "Active"))
            throw new InvalidOperationException("Thành viên đã tham gia hoặc đang có lời mời.");
        var now = DateTime.UtcNow;
        if (member == null)
            db.FreelancerGroupMembers.Add(new FreelancerGroupMember
            {
                FreelancerGroupId = groupId, UserAccountId = target.Id, Status = "Invited", InvitedAtUtc = now
            });
        else
        {
            member.Status = "Invited";
            member.InvitedAtUtc = now;
            member.RespondedAtUtc = null;
        }
        Record(groupId, leaderId, "Invited freelancer #" + target.Id);
        NotificationService.Queue(db, target.Id, "Lời mời vào nhóm: " + group.Name, "/Groups/Index");
        db.SaveChanges();
        }

        public void Respond(int groupId, int userId, bool accept)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                var group = LockGroup(groupId);
                var member = db.FreelancerGroupMembers.SingleOrDefault(m =>
                    m.FreelancerGroupId == groupId && m.UserAccountId == userId);
                if (member == null || member.Status != "Invited")
                    throw new InvalidOperationException("Lời mời không còn hiệu lực.");
                member.Status = accept ? "Active" : "Declined";
                member.RespondedAtUtc = DateTime.UtcNow;
                Record(groupId, userId, accept ? "Accepted group invitation" : "Declined group invitation");
                NotificationService.Queue(db, group.LeaderId, "Thành viên " + db.Users.Where(u => u.Id == userId).Select(u => u.Name).Single() + " " + (accept ? "đã chấp nhận" : "đã từ chối") + " lời mời", "/Groups/Details/" + groupId);
                db.SaveChanges();
                tx.Commit();
            }
        }

        public void ChangeMembership(int groupId, int actorId, int memberId, bool leave)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                // Match bid operations' Project -> Group lock ordering.
                var projectIds = db.GroupBids.Where(b => b.FreelancerGroupId == groupId && (b.Status == "Pending" || b.Status == "Accepted")).Select(b => b.ProjectId).Distinct().OrderBy(i => i).ToArray();
                foreach (var projectId in projectIds)
                    db.Database.SqlQuery<int>("SELECT Id FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", projectId).Single();
                var group = LockGroup(groupId);
                if (db.Contracts.Any(c=>c.GroupBid.FreelancerGroupId==groupId && (c.Status=="AwaitingSignatures" || c.Status=="AwaitingEscrow" || c.Status=="Active" || c.Status=="Disputed")) ||
                    db.GroupBids.Any(b => b.FreelancerGroupId == groupId && b.Status == "Accepted" && !db.Contracts.Any(c=>c.GroupBidId==b.Id) &&
                    (b.Project.Status == "AwaitingEscrow" || b.Project.Status == "InProgress" || b.Project.Status == "Disputed")))
                    throw new InvalidOperationException("Không thể đổi thành viên khi nhóm có hợp đồng đang hoạt động. Hãy gửi khiếu nại để quản trị xử lý.");
                if ((!leave && group.LeaderId != actorId) || (leave && memberId != actorId))
                    throw new UnauthorizedAccessException();
                if (memberId == group.LeaderId) throw new InvalidOperationException("Leader không thể rời hoặc bị xóa khỏi nhóm.");
                var member = db.FreelancerGroupMembers.SingleOrDefault(m =>
                    m.FreelancerGroupId == groupId && m.UserAccountId == memberId);
                if (member == null || member.Status != "Active") throw new InvalidOperationException("Thành viên không còn trong nhóm.");
                member.Status = leave ? "Left" : "Removed";
                member.RespondedAtUtc = DateTime.UtcNow;
                Record(groupId, actorId, leave ? "Left group" : "Removed freelancer #" + memberId);
                if (!leave) NotificationService.Queue(db, memberId, "Bạn đã được rời khỏi nhóm " + group.Name, "/Groups/Index");
                db.SaveChanges();
                tx.Commit();
            }
        }
    }
}
