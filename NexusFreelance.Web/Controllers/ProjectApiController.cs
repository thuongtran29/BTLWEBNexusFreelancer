using System;
using System.Data;
using System.Linq;
using System.Web;
using System.Web.Helpers;
using System.Web.Mvc;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Controllers
{
    // Cookie authentication requires the same anti-forgery cookie/header pair as the UI.
    // Explicit DTOs prevent binding ownership, publication, contract or ledger fields.
    public class ProjectApiController : BaseController
    {
        private ActionResult Reply(object value, int status)
        {
            Response.StatusCode = status; Response.TrySkipIisCustomErrors = true;
            Response.SuppressFormsAuthenticationRedirect = true; Response.Cache.SetNoStore();
            return status == 204 ? (ActionResult)new EmptyResult() : Json(value, JsonRequestBehavior.AllowGet);
        }
        private ActionResult Error(int status, string code, string message, object details = null) =>
            Reply(new { error = new { code, message, details } }, status);
        private ActionResult Writer()
        {
            if (CurrentUser == null) return Error(401, "AUTH_REQUIRED", "Đăng nhập để tiếp tục.");
            if (CurrentUser.Role != "Client") return Error(403, "CLIENT_REQUIRED", "Chỉ người thuê được chỉnh sửa dự án.");
            try { AntiForgery.Validate(Request.Cookies[AntiForgeryConfig.CookieName]?.Value, Request.Headers["X-CSRF-Token"]); }
            catch (HttpAntiForgeryException) { return Error(400, "CSRF_INVALID", "Phiên biểu mẫu không hợp lệ; tải lại trang."); }
            return null;
        }
        protected override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            if (context.Result != null && Response.StatusCode >= 400)
                context.Result = Error(Response.StatusCode, "INVALID_REQUEST", "Yêu cầu hoặc phiên đăng nhập không hợp lệ.");
        }
        protected override void OnException(ExceptionContext context)
        {
            // Do not expose SQL, JSON parser or stack details in an API response.
            var error = context.Exception;
            if (error is InvalidOperationException)
                context.Result = Error(409, "STATE_CONFLICT", "Dữ liệu đã thay đổi hoặc thao tác không phù hợp giai đoạn. Đọc lại dự án trước khi thử lại.");
            else if (error is ArgumentException || error is HttpException || error is System.Web.HttpRequestValidationException)
                context.Result = Error(400, "INVALID_REQUEST", "Dữ liệu gửi lên không hợp lệ.");
            else if (error is System.Data.Common.DbException || error is System.Data.Entity.Infrastructure.DbUpdateException)
                context.Result = Error(409, "WRITE_CONFLICT", "Không thể lưu lúc này. Đọc lại trạng thái trước khi thử lại.");
            else
            {
                System.Diagnostics.Trace.TraceError("Project API failed: {0}; {1}", error.GetType().Name, error.StackTrace);
                context.Result = Error(500, "SERVER_ERROR", "Không thể xử lý yêu cầu lúc này.");
            }
            context.ExceptionHandled = true;
        }
        public ActionResult Collection()
        {
            if (Request.HttpMethod == "GET")
            {
                // Existing public paginated endpoint remains backward compatible.
                return Redirect("/api/v1/projects" + Request.Url.Query);
            }
            if (Request.HttpMethod != "POST") return Method("GET, POST");
            var denied = Writer(); if (denied != null) return denied;
            var form = new ProjectForm { SkillIds = null }; TryUpdateModel(form);
            var validation = Validate(form, false); if (validation != null) return validation;
            var project = new ProjectEditingService(Db).Create(CurrentUser.Id, form);
            Response.AppendHeader("Location", "/api/v1/projects/" + project.Id);
            return Read(project, 201);
        }
        public ActionResult Resource(int id)
        {
            if (!new[] { "GET", "PUT", "DELETE" }.Contains(Request.HttpMethod)) return Method("GET, PUT, DELETE");
            if (id < 1) return Error(400, "INVALID_ID", "Mã dự án không hợp lệ.");
            if (Request.HttpMethod != "GET") { var denied = Writer(); if (denied != null) return denied; }
            var project = Db.Projects.Find(id);
            // Private and archived projects are not disclosed to unrelated accounts.
            if (project == null) return Error(404, "NOT_FOUND", "Không tìm thấy dự án.");
            var owner = CurrentUser != null && project.ClientId == CurrentUser.Id;
            if (Request.HttpMethod == "GET")
            {
                if (project.Status == "Cancelled" || (project.Status != "Published" && !owner && CurrentUser?.Role != "Admin"))
                    return Error(404, "NOT_FOUND", "Không tìm thấy dự án.");
                return Read(project, 200);
            }
            if (!owner) return Error(404, "NOT_FOUND", "Không tìm thấy dự án.");
            var editor = new ProjectEditingService(Db);
            if (Request.HttpMethod == "DELETE")
            {
                // Logical deletion retains references and history. Never erase contracts or money.
                using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    Db.Database.SqlQuery<int>("SELECT Id FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", id).Single();
                    Db.Entry(project).Reload();
                    if (project.Status == "Cancelled" && Db.ActivityLogs.Any(a => a.ProjectId == id && a.Action == "Archived draft via REST API"))
                    { tx.Commit(); return Reply(null, 204); }
                    if (project.Status != "Draft" || Db.Contracts.Any(c => c.ProjectId == id) || Db.Proposals.Any(p => p.ProjectId == id))
                        return Error(409, "STATE_CONFLICT", "Chỉ xóa bản nháp chưa có ứng tuyển hoặc hợp đồng.");
                    if (!Matches(project)) return Error(412, "VERSION_MISMATCH", "Đọc lại dự án và gửi If-Match mới nhất.");
                    project.Status = "Cancelled";
                    Db.ActivityLogs.Add(new ActivityLog { ProjectId = id, ActorId = CurrentUser.Id, Action = "Archived draft via REST API", CreatedAtUtc = DateTime.UtcNow });
                    Db.SaveChanges(); tx.Commit(); return Reply(null, 204);
                }
            }
            if (!editor.IsEditable(project)) return Error(409, "STATE_CONFLICT", "Dự án đang khóa chỉnh sửa.");
            if (!Matches(project)) return Error(412, "VERSION_MISMATCH", "Đọc lại dự án và gửi If-Match mới nhất.");
            var form = new ProjectForm { SkillIds = null }; TryUpdateModel(form);
            var invalid = Validate(form, editor.InformationOnly(project)); if (invalid != null) return invalid;
            // The service rechecks this token under the project mutation lock.
            form.EditVersion = Request.Headers["If-Match"].Trim('"');
            editor.Save(id, CurrentUser.Id, form);
            return Read(project, 200);
        }
        private bool Matches(Project project) => Request.Headers["If-Match"] == "\"" + new ProjectEditingService(Db).Version(project) + "\"";
        private ActionResult Validate(ProjectForm form, bool informationOnly)
        {
            foreach (var issue in ProjectInputPolicy.Errors(form, Db, informationOnly)) ModelState.AddModelError(issue.Key, issue.Value);
            if (ModelState.IsValid) return null;
            return Error(422, "VALIDATION_ERROR", "Kiểm tra thông tin dự án.", ModelState.Where(x => x.Value.Errors.Count > 0)
                .ToDictionary(x => x.Key, x => x.Value.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Giá trị không hợp lệ." : e.ErrorMessage).ToArray()));
        }
        private ActionResult Read(Project project, int status)
        {
            var skills = Db.ProjectSkillRequirements.Where(s => s.ProjectId == project.Id).OrderBy(s => s.SkillTrackId)
                .Select(s => new { skillId = s.SkillTrackId, minimumLevel = s.MinimumLevel }).ToList();
            var owner = CurrentUser != null && (project.ClientId == CurrentUser.Id || CurrentUser.Role == "Admin");
            var version = owner ? new ProjectEditingService(Db).Version(project) : null;
            if (version != null) Response.AppendHeader("ETag", "\"" + version + "\"");
            return Reply(new { id = project.Id, title = project.Title, description = project.Description,
                industry = project.Industry, projectType = project.ProjectType, budget = project.Budget,
                deadline = project.DeadlineUtc?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                status = project.Status, createdAtUtc = DateTime.SpecifyKind(project.CreatedAtUtc, DateTimeKind.Utc).ToString("o"), skills,
                canEdit = CurrentUser?.Role == "Client" && project.ClientId == CurrentUser.Id && new ProjectEditingService(Db).IsEditable(project) }, status);
        }
        private ActionResult Method(string allow) { Response.AppendHeader("Allow", allow); return Error(405, "METHOD_NOT_ALLOWED", "Phương thức HTTP không được hỗ trợ."); }
    }
}
