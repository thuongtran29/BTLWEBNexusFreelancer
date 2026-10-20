using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace NexusFreelance.Web.Controllers
{
    public class ProfileImageController : BaseController
    {
        private string ImagePath(int id) => Path.Combine(Server.MapPath("~/App_Data/ProfileImages"), id + ".jpg");

        [HttpGet]
        public ActionResult Show(int id)
        {
            var visible = CurrentUser?.Id == id || CurrentUser?.Role == "Admin" || Db.FreelancerProfiles.Any(p => p.UserAccountId == id &&
                (p.IsPublic == null || p.IsPublic == true) && !p.User.IsSuspended && p.User.EmailVerifiedAtUtc.HasValue);
            if (!visible && CurrentUser != null)
            {
                var viewer = CurrentUser.Id;
                visible = Db.DirectConversations.Any(c => (c.ClientId == viewer && c.FreelancerId == id) || (c.ClientId == id && c.FreelancerId == viewer)) ||
                    Db.FreelancerGroupMembers.Any(m => m.UserAccountId == viewer && m.Status == "Active" &&
                        Db.FreelancerGroupMembers.Any(peer => peer.FreelancerGroupId == m.FreelancerGroupId && peer.UserAccountId == id && peer.Status == "Active")) ||
                    Db.Contracts.Any(c => (c.Project.ClientId == viewer && (c.CounterpartyId == id || Db.ContractMembers.Any(m => m.ContractId == c.Id && m.UserAccountId == id))) ||
                        (c.Project.ClientId == id && (c.CounterpartyId == viewer || Db.ContractMembers.Any(m => m.ContractId == c.Id && m.UserAccountId == viewer))));
            }
            if (!visible) return HttpNotFound();
            var path = ImagePath(id);
            if (!System.IO.File.Exists(path)) return HttpNotFound();
            Response.Cache.SetNoStore();
            Response.AppendHeader("X-Content-Type-Options", "nosniff");
            return File(System.IO.File.ReadAllBytes(path), "image/jpeg");
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult Upload(HttpPostedFileBase image, string returnUrl)
        {
            if (CurrentUser == null) return Forbidden();
            try
            {
                if (image == null || image.ContentLength < 1 || image.ContentLength > 2 * 1024 * 1024)
                    throw new ArgumentException("Chọn ảnh PNG hoặc JPEG tối đa 2 MB.");
                var ext = Path.GetExtension(image.FileName ?? "").ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
                    throw new ArgumentException("Chỉ nhận PNG hoặc JPEG.");
                using (var buffer = new MemoryStream())
                {
                    var block = new byte[8192]; int n;
                    while ((n = image.InputStream.Read(block, 0, block.Length)) > 0)
                    {
                        if (buffer.Length + n > 2 * 1024 * 1024) throw new ArgumentException("Ảnh vượt 2 MB.");
                        buffer.Write(block, 0, n);
                    }
                    if (buffer.Length != image.ContentLength) throw new ArgumentException("Tệp ảnh không đầy đủ.");
                    buffer.Position = 0;
                    // Read dimensions first; decode pixels only after rejecting oversized headers.
                    using (var original = Image.FromStream(buffer, false, false))
                    {
                        if ((original.RawFormat.Guid != ImageFormat.Jpeg.Guid && original.RawFormat.Guid != ImageFormat.Png.Guid) ||
                            original.Width > 4096 || original.Height > 4096 || original.Width < 1 || original.Height < 1)
                            throw new ArgumentException("Ảnh phải là PNG/JPEG thật, tối đa 4096 × 4096 pixel.");
                        var scale = Math.Min(1d, 512d / Math.Max(original.Width, original.Height));
                        using (var resized = new Bitmap(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale))))
                        using (var graphics = Graphics.FromImage(resized))
                        {
                            graphics.Clear(Color.White); graphics.DrawImage(original, 0, 0, resized.Width, resized.Height);
                            var path = ImagePath(CurrentUser.Id);
                            Directory.CreateDirectory(Path.GetDirectoryName(path));
                            // Re-encode pixels only: filenames, original metadata and appended content are discarded.
                            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            try
                            {
                                resized.Save(temporary, ImageFormat.Jpeg);
                                if (System.IO.File.Exists(path)) System.IO.File.Replace(temporary, path, null);
                                else System.IO.File.Move(temporary, path);
                            }
                            finally { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
                        }
                    }
                }
                TempData["Notice"] = "Đã cập nhật ảnh đại diện.";
            }
            catch (ArgumentException) { TempData["Error"] = "Ảnh không hợp lệ. Dùng PNG/JPEG tối đa 2 MB và 4096 × 4096 pixel."; }
            catch (System.Runtime.InteropServices.ExternalException) { TempData["Error"] = "Không thể đọc ảnh. Hãy chọn ảnh PNG/JPEG khác."; }
            catch (IOException)
            {
                System.Diagnostics.Trace.TraceWarning("Profile image storage update could not complete.");
                TempData["Error"] = "Chưa lưu được ảnh. Hãy thử lại sau vài giây.";
            }
            catch (UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceWarning("Profile image storage permission denied.");
                TempData["Error"] = "Chưa lưu được ảnh. Vui lòng liên hệ quản trị để kiểm tra nơi lưu ảnh.";
            }
            if (Url.IsLocalUrl(returnUrl) && !returnUrl.StartsWith("/ProfileImage", StringComparison.OrdinalIgnoreCase)) return Redirect(returnUrl);
            return RedirectToAction("Security", "Account");
        }
    }
}
