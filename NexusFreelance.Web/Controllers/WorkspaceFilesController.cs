using System;
using System.Data;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web;
using System.Web.Mvc;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Controllers
{
    [Authorize]
    public class WorkspaceFilesController : BaseController
    {
        private const int MaxFileBytes = 8 * 1024 * 1024;
        private static readonly string[] AllowedExtensions =
        {
            ".pdf", ".png", ".jpg", ".jpeg", ".txt", ".zip", ".docx", ".xlsx"
        };

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Upload(WorkspaceFileUploadForm form, HttpPostedFileBase upload)
        {
            if (form == null || form.WorkspaceId <= 0 || (form.FileId.HasValue && form.FileId.Value <= 0))
                return new HttpStatusCodeResult(400);
            var workspace = Db.Workspaces.Include(w => w.Project)
                .SingleOrDefault(w => w.Id == form.WorkspaceId);
            if (workspace == null) return HttpNotFound();
            var actor = CurrentUser;
            if (actor == null || !new ContractService(Db).CanAccess(workspace,actor.Id))
                return Forbidden();
            if (!new ContractService(Db).CanWork(workspace)) return new HttpStatusCodeResult(409);

            var name = upload == null ? "" : Path.GetFileName((upload.FileName ?? "").Replace('\\', '/')).Trim();
            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (upload == null || upload.ContentLength < 1 || upload.ContentLength > MaxFileBytes ||
                name.Length < 1 || name.Length > 180 || name.Any(c => char.IsControl(c)) ||
                !AllowedExtensions.Contains(extension))
                return new HttpStatusCodeResult(400, "Chỉ nhận PDF, ảnh, TXT, ZIP, DOCX, XLSX (tối đa 8 MB).");

            byte[] bytes;
            using (var memory = new MemoryStream())
            {
                var block = new byte[8192];
                int count;
                while ((count = upload.InputStream.Read(block, 0, block.Length)) > 0)
                {
                    if (memory.Length + count > MaxFileBytes) return new HttpStatusCodeResult(400);
                    memory.Write(block, 0, count);
                }
                bytes = memory.ToArray();
            }
            if (bytes.Length == 0 || bytes.Length != upload.ContentLength) return new HttpStatusCodeResult(400);

            string contentType;
            try { contentType = WorkspaceContentPolicy.Validate(name, bytes); }
            catch (InvalidDataException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction("Index", "Workspace", new { id = workspace.Id });
            }

            string digest;
            using (var hash = SHA256.Create())
                digest = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");

            using (var transaction = Db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                var status = Db.Database.SqlQuery<string>(
                    "SELECT [Status] FROM dbo.Projects WITH (UPDLOCK,HOLDLOCK) WHERE Id=@p0", workspace.ProjectId)
                    .SingleOrDefault();
                if (!new ContractService(Db).CanWork(workspace)) return new HttpStatusCodeResult(409);

                WorkspaceFile item;
                var versionNumber = 1;
                if (form.FileId.HasValue)
                {
                    item = Db.WorkspaceFiles.SqlQuery(
                        "SELECT * FROM dbo.WorkspaceFiles WITH (UPDLOCK,HOLDLOCK) WHERE Id=@p0", form.FileId.Value)
                        .SingleOrDefault();
                    if (item == null || item.WorkspaceId != workspace.Id) return Forbidden();
                    versionNumber = (Db.WorkspaceFileVersions.Where(v => v.WorkspaceFileId == item.Id)
                        .Max(v => (int?)v.VersionNumber) ?? 0) + 1;
                }
                else
                {
                    item = new WorkspaceFile { WorkspaceId = workspace.Id,
                        CreatedById = actor.Id, CreatedAtUtc = DateTime.UtcNow };
                    Db.WorkspaceFiles.Add(item);
                    Db.SaveChanges();
                }

                Db.WorkspaceFileVersions.Add(new WorkspaceFileVersion
                {
                    WorkspaceFileId = item.Id, VersionNumber = versionNumber,
                    UploadedById = actor.Id, FileName = name, ContentType = contentType,
                    ByteLength = bytes.Length, Sha256 = digest, Data = bytes,
                    CreatedAtUtc = DateTime.UtcNow
                });
                Db.ActivityLogs.Add(new ActivityLog { ProjectId = workspace.ProjectId, ActorId = actor.Id,
                    Action = "Uploaded workspace file #" + item.Id + " v" + versionNumber,
                    CreatedAtUtc = DateTime.UtcNow });
                if (workspace.Project.ClientId != actor.Id)
                    NotificationService.Queue(Db, workspace.Project.ClientId,
                        "Nhóm đã tải tài liệu mới lên Workspace", "/Workspace/Index/" + workspace.Id);
                Db.SaveChanges();
                transaction.Commit();
            }
            TempData["Notice"] = "Đã lưu tệp và lịch sử phiên bản.";
            return RedirectToAction("Index", "Workspace", new { id = workspace.Id });
        }

        [HttpGet]
        public ActionResult Download(int id)
        {
            var file = Db.WorkspaceFileVersions.AsNoTracking().Where(v => v.Id == id)
                .Select(v => new
                {
                    v.FileName,
                    v.File.Workspace.Project.ClientId,
                    v.File.Workspace.ProjectTeamId,
                    v.File.WorkspaceId
                }).SingleOrDefault();
            if (file == null) return HttpNotFound();
            var actor = CurrentUser;
            var workspace=Db.Workspaces.Include(w=>w.Project).Single(w=>w.Id==file.WorkspaceId);
            if (actor == null || (actor.Role != "Admin" && !new ContractService(Db).CanAccess(workspace,actor.Id)))
                return Forbidden();
            var content = Db.WorkspaceFileVersions.AsNoTracking()
                .Where(v => v.Id == id).Select(v => v.Data).Single();
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            Response.AppendHeader("X-Content-Type-Options", "nosniff");
            return File(content, "application/octet-stream", file.FileName);
        }
    }
}
