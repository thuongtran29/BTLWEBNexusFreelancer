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
    // Implementations record their actual generator so every saved version stays auditable.
    public interface IJobDraftGenerator
    {
        string GeneratorName { get; }
        JobEditForm Generate(JobIdeaForm idea);
    }

    public sealed class RuleBasedJobDraftGenerator : IJobDraftGenerator
    {
        public string GeneratorName { get { return "RuleBasedDemo-v2"; } }
        public JobEditForm Generate(JobIdeaForm form) => BriefComposer.Compose(form);
    }

    public sealed class JobStudioService
    {
        private readonly AppDbContext db;
        private readonly IJobDraftGenerator generator;

        public JobStudioService(AppDbContext db, IJobDraftGenerator generator)
        {
            this.db = db;
            this.generator = generator;
        }

        public AIJobDraft Generate(JobIdeaForm form, int clientId)
        {
            var suggestion = GenerateSuggestion(form, generator);
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                db.Database.SqlQuery<int>("SELECT Id FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", clientId).Single();
                var recent = db.AIJobDraftVersions.Include(v => v.Roles).Where(v => v.Draft.ClientId == clientId)
                    .OrderByDescending(v => v.Id).Take(10).ToList();
                for (var retry = 0; recent.Any(v => Fingerprint(v.Title, v.Description, v.Objectives, v.Scope, v.UserStories, v.RequiredSkills, v.Deliverables, v.Milestones) == Fingerprint(suggestion.Title, suggestion.Description, suggestion.Objectives, suggestion.Scope, suggestion.UserStories, suggestion.RequiredSkills, suggestion.Deliverables, suggestion.Milestones)); retry++)
                {
                    if (form.UseExternalAi || retry >= 3) throw new InvalidOperationException("Kết quả đang trùng bản gần đây. Chưa lưu nháp trùng; hãy làm rõ yêu cầu hoặc tạo lại để nhận góc soạn khác.");
                    suggestion = GenerateSuggestion(form, generator);
                }
                var version = NewVersion(suggestion, 1, generator.GeneratorName, form.ProjectType);
                version.PromptVersion = JobPrompt.Version;
                version.ExternalAiRequested = form.UseExternalAi;
                version.KnowledgeReferences = JobDraftGeneratorFactory.IsExternalOrigin(generator.GeneratorName) ? form.KnowledgeReferences : form.SharedKnowledgeReferences;
                var draft = new AIJobDraft { ClientId = clientId, Idea = form.Idea.Trim(), Industry = IndustryCatalog.Normalize(form.Industry), ProjectType = form.ProjectType,
                    Budget = form.Budget, DeadlineUtc = form.Deadline.HasValue ? (DateTime?)DateTime.SpecifyKind(form.Deadline.Value.Date, DateTimeKind.Utc) : null, CreatedAtUtc = DateTime.UtcNow };
                draft.Versions.Add(version); db.AIJobDrafts.Add(draft); db.SaveChanges(); tx.Commit(); return draft;
            }
        }
        private static string Fingerprint(params string[] fields) => System.Text.RegularExpressions.Regex.Replace(string.Join("|", fields), @"\s+", " ").ToLowerInvariant();

        internal static JobEditForm GenerateSuggestion(JobIdeaForm form, IJobDraftGenerator generator)
        {
            if (form == null || generator == null) throw new ArgumentNullException(form == null ? "form" : "generator");
            if (!form.UseExternalAi && (generator is IExternalJobDraftGenerator ||
                JobDraftGeneratorFactory.IsExternalOrigin(generator.GeneratorName)))
                throw new InvalidOperationException("Cần đồng ý gửi dữ liệu tới AI bên ngoài trước khi tạo JD.");
            var suggestion = generator.Generate(form);
            if (form.UseExternalAi && !JobDraftGeneratorFactory.IsExternalOrigin(generator.GeneratorName))
                throw new InvalidOperationException((generator as IExternalJobDraftGenerator)?.FailureMessage ?? "AI chưa trả về JD hợp lệ. Chưa lưu bản nháp. Hãy thử lại sau hoặc chủ động bỏ chọn AI để dùng mẫu nội bộ.");
            ValidateDraft(suggestion, form.ProjectType);
            return suggestion;
        }

        private static void ValidateDraft(JobEditForm form, string projectType, bool allowEmptyRoleRows = false)
        {
            if (form == null) throw new InvalidOperationException("JD thiếu nội dung. Hãy tạo lại hoặc bổ sung các mục bắt buộc.");
            var fields = new[] { form.Title, form.Description, form.Objectives, form.Scope,
                form.UserStories, form.RequiredSkills, form.Deliverables, form.Milestones };
            var limits = new[] { 180, 4000, 2000, 2000, 2000, 1000, 2000, 2000 };
            for (var i = 0; i < fields.Length; i++)
                if (string.IsNullOrWhiteSpace(fields[i]) || fields[i].Length > limits[i])
                    throw new InvalidOperationException("JD có nội dung trống hoặc vượt độ dài cho phép. Hãy kiểm tra từng mục.");
            BuildProjectDescription(fields.Skip(1).Select(s => s.Trim()).ToArray());
            if (form.Roles == null || form.Roles.Count < 1 || form.Roles.Count > 6)
                throw new InvalidOperationException("JD cần từ 1 đến 6 vị trí tuyển dụng.");
            // The editor pads unused rows; validate only named rows, while rejecting malformed objects.
            if (form.Roles.Any(r => r == null || (r.Title ?? "").Length > 100 ||
                (!allowEmptyRoleRows && string.IsNullOrWhiteSpace(r.Title))))
                throw new InvalidOperationException("Dữ liệu vị trí tuyển dụng không hợp lệ.");
            var roles = form.Roles.Where(r => !string.IsNullOrWhiteSpace(r.Title)).ToList();
            if (roles.Count == 0 || roles.Any(r => r.RequiredCount < 1 || r.RequiredCount > 50))
                throw new InvalidOperationException("Mỗi vị trí cần tên và số lượng từ 1 đến 50.");
            if (roles.Select(r => r.Title.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != roles.Count)
                throw new InvalidOperationException("Các vị trí đề xuất không được trùng tên.");
            if (projectType == "SOLO" && (roles.Count != 1 || roles[0].RequiredCount != 1))
                throw new InvalidOperationException("Dự án SOLO chỉ được tuyển một vị trí với một người.");
        }

        private static AIJobDraftVersion NewVersion(JobEditForm form, int number, string origin, string projectType)
        {
            ValidateDraft(form, projectType, origin == "ClientEdited");
            var version = new AIJobDraftVersion
            {
                VersionNumber = number, Generator = origin,
                Title = form.Title.Trim(), Description = form.Description.Trim(),
                Objectives = form.Objectives.Trim(), Scope = form.Scope.Trim(),
                UserStories = form.UserStories.Trim(), RequiredSkills = form.RequiredSkills.Trim(),
                Deliverables = form.Deliverables.Trim(), Milestones = form.Milestones.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            };
            foreach (var role in form.Roles.Where(r => !string.IsNullOrWhiteSpace(r.Title)))
            {
                var title = role.Title.Trim();
                version.Roles.Add(new AIJobRoleSuggestion
                {
                    Title = title, RequiredCount = role.RequiredCount
                });
            }
            return version;
        }

        // Retain the approved specification in the normal Project so the workspace can use it.
        // The original structured versions remain available on the Job Studio draft.
        private static string BuildProjectDescription(AIJobDraftVersion version)
        {
            return BuildProjectDescription(new[] { version.Description, version.Objectives, version.Scope,
                version.UserStories, version.RequiredSkills, version.Deliverables, version.Milestones });
        }

        private static string BuildProjectDescription(string[] fields)
        {
            var labels = new[] { "MÔ TẢ", "MỤC TIÊU", "PHẠM VI", "USER STORIES", "KỸ NĂNG", "BÀN GIAO", "CỘT MỐC" };
            var description = string.Join("\r\n\r\n", labels.Select((label, i) => label + "\r\n" + fields[i]));
            if (description.Length > 4000)
                throw new InvalidOperationException(
                    "Đặc tả vượt giới hạn 4.000 ký tự của mô tả dự án. Hãy rút gọn nội dung trong Job Studio trước khi tạo dự án.");
            return description;
        }

        private AIJobDraft LockOwnedDraft(int id, int clientId)
        {
            // A row lock serializes edits and Apply to avoid duplicate projects or lost versions.
            var locked = db.Database.SqlQuery<int>(
                "SELECT [Id] FROM [dbo].[AIJobDrafts] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0 AND [ClientId] = @p1 AND [IsDeleted] = 0",
                id, clientId).SingleOrDefault();
            if (locked == 0) throw new InvalidOperationException("Không tìm thấy bản nháp được phép quản lý.");
            return db.AIJobDrafts.Single(d => d.Id == id && d.ClientId == clientId);
        }

        public void SaveRevision(JobEditForm form, int clientId)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                var draft = LockOwnedDraft(form.Id, clientId);
                if (draft.AppliedProjectId.HasValue)
                    throw new InvalidOperationException("Bản nháp đã chuyển thành dự án, hãy chỉnh sửa dự án tại trang quản lý.");
                var current = db.AIJobDraftVersions.Where(v => v.AIJobDraftId == form.Id)
                    .OrderByDescending(v => v.VersionNumber).First();
                if (current.Id != form.ExpectedVersionId)
                    throw new InvalidOperationException("Bản nháp đã được cập nhật ở phiên khác. Tải lại trang trước khi sửa.");
                var revised = NewVersion(form, current.VersionNumber + 1, "ClientEdited", draft.ProjectType);
                revised.PromptVersion = current.PromptVersion;
                revised.KnowledgeReferences = current.KnowledgeReferences;
                revised.ExternalAiRequested = current.ExternalAiRequested;
                revised.AIJobDraftId = form.Id;
                db.AIJobDraftVersions.Add(revised);
                db.SaveChanges();
                tx.Commit();
            }
        }

        public int ApplyToProject(int id, int clientId)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                var draft = LockOwnedDraft(id, clientId);
                if (draft.AppliedProjectId.HasValue)
                    return draft.AppliedProjectId.Value; // Idempotent under concurrent double-clicks.
                var version = db.AIJobDraftVersions.Include(v => v.Roles)
                    .Where(v => v.AIJobDraftId == id)
                    .OrderByDescending(v => v.VersionNumber).First();
                if (version.Roles.Count == 0)
                    throw new InvalidOperationException("Cần ít nhất một vị trí trước khi chuyển thành dự án.");
                var projectDescription = BuildProjectDescription(version);
                var project = new Project
                {
                    ClientId = clientId, Title = version.Title, Description = projectDescription,
                    Industry = draft.Industry, ProjectType = draft.ProjectType, Budget = draft.Budget,
                    DeadlineUtc = draft.DeadlineUtc, Status = "Draft", CreatedAtUtc = DateTime.UtcNow
                };
                db.Projects.Add(project);
                foreach (var role in version.Roles)
                    db.RoleRequirements.Add(new RoleRequirement
                    {
                        Project = project, Title = role.Title, RequiredCount = role.RequiredCount
                    });
                db.SaveChanges();
                var tracks = db.SkillTracks.AsNoTracking().Where(t => t.IsActive != false).ToList();
                var inferred = version.Roles.SelectMany(r => RoleSkillResolver.ForRole(new RoleRequirement { ProjectId = project.Id, Title = r.Title }, tracks, new List<ProjectSkillRequirement>()))
                    .GroupBy(s => s.SkillTrackId).Select(g => g.First()).ToList();
                foreach (var skill in inferred) db.ProjectSkillRequirements.Add(new ProjectSkillRequirement { ProjectId = project.Id, SkillTrackId = skill.SkillTrackId, MinimumLevel = skill.MinimumLevel });
                draft.AppliedProjectId = project.Id;
                db.ActivityLogs.Add(new ActivityLog
                {
                    ProjectId = project.Id, ActorId = clientId, CreatedAtUtc = DateTime.UtcNow,
                    Action = "Created project draft from Job Studio draft #" + draft.Id
                });
                db.SaveChanges();
                tx.Commit();
                return project.Id;
            }
        }
    }
}
