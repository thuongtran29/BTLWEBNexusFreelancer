using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public sealed class JobStudioController : BaseController
    {
        private JobStudioService Studio(bool clientOptedIn = false, string expectedProvider = null) =>
            new JobStudioService(Db, JobDraftGeneratorFactory.Create(clientOptedIn, expectedProvider));

        private bool IsClient => CurrentUser?.Role == "Client";

        [HttpGet]
        public ActionResult Index()
        {
            if (!IsClient) return Forbidden();
            return View(Db.AIJobDrafts.Include(d => d.AppliedProject)
                .Where(d => d.ClientId == CurrentUser.Id && !d.IsDeleted).OrderByDescending(d => d.CreatedAtUtc).ToList());
        }

        [HttpGet]
        public ActionResult New()
        {
            if (!IsClient) return Forbidden();
            return View(new JobIdeaForm { UseProjectKnowledge = JobDraftGeneratorFactory.SelectedProvider != "Gemini" });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Generate([Bind(Include="Industry,Idea,ProjectType,Budget,Deadline,UseExternalAi,ExternalAiProvider,UseProjectKnowledge,UseSharedKnowledge,AdditionalInstructions")] JobIdeaForm form)
        {
            if (!IsClient) return Forbidden();
            if (form == null)
            {
                ModelState.AddModelError("", "Dữ liệu ý tưởng không hợp lệ.");
                return View("New", new JobIdeaForm());
            }
            if (!IndustryCatalog.IsValid(form.Industry)) ModelState.AddModelError("Industry", "Nhóm ngành không hợp lệ.");
            if (form.ProjectType != "SOLO" && form.ProjectType != "TEAM" && form.ProjectType != "ENTERPRISE")
                ModelState.AddModelError("ProjectType", "Loại dự án không hợp lệ.");
            if (string.IsNullOrWhiteSpace(form.Idea) || form.Idea.Trim().Length < 10)
                ModelState.AddModelError("Idea", "Mô tả ý tưởng ít nhất 10 ký tự.");
            if (form.Deadline.HasValue && form.Deadline.Value.Date < DateTime.Today)
                ModelState.AddModelError("Deadline", "Hạn phải từ hôm nay trở đi.");
            if (form.UseExternalAi && form.ExternalAiProvider != JobDraftGeneratorFactory.SelectedProvider) {
                ModelState.AddModelError("", "Nhà cung cấp AI đã thay đổi hoặc chưa được xác nhận. Tải lại trang và chọn đồng ý gửi dữ liệu.");
                ModelState.Remove("UseExternalAi"); form.UseExternalAi = false;
            }
            if (!ModelState.IsValid) return View("New", form);

            if(form.UseExternalAi && !JobDraftGeneratorFactory.IsExternalAiConfigured) { ModelState.AddModelError("", "AI chưa được cấu hình đầy đủ. Bạn có thể dùng mẫu nội bộ hoặc cấu hình API trước."); return View("New", form); }
            var cutoff = DateTime.UtcNow.AddHours(-1);
            if(Db.AIJobDrafts.Count(d => d.ClientId == CurrentUser.Id && d.CreatedAtUtc >= cutoff) >= 10) { ModelState.AddModelError("", "Bạn đã tạo 10 bản nháp trong giờ qua. Hãy chỉnh sửa các bản hiện có trước khi tạo thêm."); return View("New", form); }
            JobPrompt.AttachKnowledge(Db, CurrentUser.Id, form);
            AIJobDraft draft;
            try { draft = Studio(form.UseExternalAi, form.ExternalAiProvider).Generate(form, CurrentUser.Id); }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); return View("New", form); }
            var origin = Db.AIJobDraftVersions.Where(v => v.AIJobDraftId == draft.Id)
                .Select(v => v.Generator).FirstOrDefault();
            TempData["Notice"] = JobDraftGeneratorFactory.IsExternalOrigin(origin)
                ? "Đã tạo gợi ý bằng AI. Hãy kiểm tra thông tin, phạm vi và chỉnh sửa trước khi sử dụng."
                : "Đã tạo mẫu nội bộ để bạn biên soạn. Mẫu này không phải kết quả AI.";
            return RedirectToAction("Edit", new { id = draft.Id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            if(!IsClient) return Forbidden();
            using(var tx = Db.Database.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
            {
                var locked = Db.Database.SqlQuery<int>("SELECT Id FROM dbo.AIJobDrafts WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0 AND ClientId=@p1 AND IsDeleted=0", id, CurrentUser.Id).SingleOrDefault();
                if(locked == 0) return HttpNotFound();
                var draft = Db.AIJobDrafts.Single(d => d.Id == id);
                if(draft.AppliedProjectId.HasValue) return new HttpStatusCodeResult(409, "Bản nháp đã tạo dự án, không thể xóa lịch sử nguồn.");
                draft.IsDeleted = true; Db.SaveChanges(); tx.Commit();
            }
            TempData["Notice"] = "Đã xóa bản nháp khỏi danh sách.";
            return RedirectToAction("Index");
        }

        private AIJobDraft OwnedDraft(int id)
        {
            return Db.AIJobDrafts.Include(d => d.AppliedProject)
                .SingleOrDefault(d => d.Id == id && d.ClientId == CurrentUser.Id && !d.IsDeleted);
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            if (!IsClient) return Forbidden();
            var draft = OwnedDraft(id);
            if (draft == null) return HttpNotFound();
            var version = Db.AIJobDraftVersions.Include(v => v.Roles)
                .Where(v => v.AIJobDraftId == id).OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (version == null) return HttpNotFound();
            var form = new JobEditForm
            {
                Id = draft.Id, ExpectedVersionId = version.Id,
                Title = version.Title, Description = version.Description,
                Objectives = version.Objectives, Scope = version.Scope,
                UserStories = version.UserStories, RequiredSkills = version.RequiredSkills,
                Deliverables = version.Deliverables, Milestones = version.Milestones,
                Roles = version.Roles.OrderBy(r => r.Id)
                    .Select(r => new JobRoleForm { Title = r.Title, RequiredCount = r.RequiredCount }).ToList()
            };
            // SOLO has one hire; applied drafts are read-only and need no empty editor rows.
            if (!draft.AppliedProjectId.HasValue && draft.ProjectType != "SOLO")
                while (form.Roles.Count < 6) form.Roles.Add(new JobRoleForm());
            SetEditMetadata(draft, version.VersionNumber);
            return View(form);
        }

        private void SetEditMetadata(AIJobDraft draft, int versionNumber)
        {
            ViewBag.Draft = draft;
            ViewBag.VersionNumber = versionNumber;
            var version = Db.AIJobDraftVersions.Single(v => v.AIJobDraftId == draft.Id && v.VersionNumber == versionNumber);
            ViewBag.Generator = version.Generator;
            ViewBag.PromptVersion = version.PromptVersion;
            ViewBag.KnowledgeReferences = version.KnowledgeReferences;
            ViewBag.ExternalAiRequested = version.ExternalAiRequested;
            var preview = new Project { Id = draft.AppliedProjectId ?? 0, ClientId = draft.ClientId, Client = CurrentUser, Industry = draft.Industry, Budget = draft.Budget, ProjectType = draft.ProjectType };
            preview.Requirements = Db.AIJobRoleSuggestions.Where(r => r.AIJobDraftVersionId == version.Id).ToList()
                .Select(r => new RoleRequirement { Id = r.Id, ProjectId = preview.Id, Title = r.Title, RequiredCount = r.RequiredCount }).ToList();
            if (draft.AppliedProjectId.HasValue) preview = Db.Projects.Include(p => p.Client).Include(p => p.Requirements).Single(p => p.Id == draft.AppliedProjectId.Value);
            ViewBag.PositionPreview = new PositionMatchingService(Db).ForProject(preview);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Edit(JobEditForm form)
        {
            if (!IsClient) return Forbidden();
            if (form == null) return new HttpStatusCodeResult(400, "Dữ liệu chỉnh sửa không hợp lệ.");
            var draft = OwnedDraft(form.Id);
            if (draft == null) return HttpNotFound();
            if (draft.AppliedProjectId.HasValue) return new HttpStatusCodeResult(409, "Bản nháp đã được sử dụng.");

            var fields = new[] { form.Title, form.Description, form.Objectives, form.Scope,
                form.UserStories, form.RequiredSkills, form.Deliverables, form.Milestones };
            if (fields.Any(string.IsNullOrWhiteSpace))
                ModelState.AddModelError("", "Các nội dung chính không được để trống.");
            if (form.Roles == null || form.Roles.Count > 6 || form.Roles.Count == 0)
                ModelState.AddModelError("Roles", "Chỉ hỗ trợ tối đa 6 vị trí.");
            else
            {
                if (form.Roles.Any(r => r == null))
                {
                    ModelState.AddModelError("Roles", "Dữ liệu vị trí không hợp lệ.");
                    form.Roles = form.Roles.Select(r => r ?? new JobRoleForm()).ToList();
                }
                var named = form.Roles.Where(r => !string.IsNullOrWhiteSpace(r.Title)).ToList();
                if (named.Count == 0) ModelState.AddModelError("Roles", "Cần ít nhất một vị trí.");
                if (named.Any(r => r.RequiredCount < 1 || r.RequiredCount > 50))
                    ModelState.AddModelError("Roles", "Số lượng cho từng vị trí phải từ 1 đến 50.");
                if (named.Select(r => r.Title.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != named.Count)
                    ModelState.AddModelError("Roles", "Tên vị trí không được trùng.");
            }

            if (!ModelState.IsValid)
            {
                SetEditMetadata(draft, Db.AIJobDraftVersions.Where(v => v.AIJobDraftId == draft.Id)
                    .Max(v => v.VersionNumber));
                return View(form);
            }

            try
            {
                Studio().SaveRevision(form, CurrentUser.Id);
                TempData["Notice"] = "Đã lưu phiên bản chỉnh sửa.";
            }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            catch (System.Data.Entity.Infrastructure.DbUpdateException)
            { TempData["Error"] = "Không thể lưu do dữ liệu vừa thay đổi. Vui lòng thử lại."; }
            return RedirectToAction("Edit", new { id = form.Id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Apply(int id)
        {
            if (!IsClient) return Forbidden();
            if (OwnedDraft(id) == null) return HttpNotFound();
            try
            {
                var projectId = Studio().ApplyToProject(id, CurrentUser.Id);
                TempData["Notice"] = "Đã tạo dự án nháp cùng các vị trí tuyển dụng. Kiểm tra lại trước khi công bố.";
                return RedirectToAction("Manage", "Projects", new { id = projectId });
            }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
            catch (System.Data.Entity.Infrastructure.DbUpdateException)
            { TempData["Error"] = "Không thể chuyển bản nháp thành dự án do dữ liệu vừa thay đổi."; }
            return RedirectToAction("Edit", new { id });
        }
    }
}
