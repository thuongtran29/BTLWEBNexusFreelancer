using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web.Mvc;
using NexusFreelance.Web.Services;

namespace NexusFreelance.Web.Controllers
{
    public class RegistrationConfirmationForm
    {
        [Required, EmailAddress, StringLength(200)] public string Email { get; set; }
        [Required, RegularExpression("Client|Freelancer")] public string Role { get; set; }
        [Required, RegularExpression("[0-9]{6}")] public string Code { get; set; }
    }

    public partial class AccountController
    {
        [HttpGet, AllowAnonymous]
        public ActionResult ConfirmRegistration(string email, string role)
        {
            if(string.IsNullOrWhiteSpace(email) || email.Length>200 || !new EmailAddressAttribute().IsValid(email) || (role!="Client" && role!="Freelancer"))return new HttpStatusCodeResult(400);
            Response.Cache.SetNoStore();Response.AppendHeader("Referrer-Policy","no-referrer");
            return View(new RegistrationConfirmationForm { Email = email.Trim().ToLowerInvariant(), Role = role });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public ActionResult ConfirmRegistration(RegistrationConfirmationForm form)
        {
            if (form == null) return new HttpStatusCodeResult(400);
            if (!ModelState.IsValid) return View(form);
            if (!new EmailCodeService(Db).Verify(form.Email.Trim().ToLowerInvariant(), form.Role, form.Code))
            { ModelState.AddModelError("", "Mã không đúng, đã hết hạn hoặc đã dùng. Sau 5 lần sai cần yêu cầu mã mới."); return View(form); }
            TempData["VerificationNotice"] = "Email đã được xác minh. Hãy đăng nhập với đúng vai trò.";
            return RedirectToAction("Login");
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public ActionResult ResendRegistrationCode(string email, string role)
        {
            if (string.IsNullOrWhiteSpace(email) || email.Length > 200 || (role != "Client" && role != "Freelancer")) return new HttpStatusCodeResult(400);
            email = email.Trim().ToLowerInvariant();
            if(!PasswordRecoveryService.IsDeliveryConfigured)
            { TempData["Error"]="Dịch vụ gửi email hiện chưa sẵn sàng. Vui lòng thử lại sau hoặc liên hệ đơn vị vận hành.";return RedirectToAction("ConfirmRegistration",new{email,role}); }
            if (PasswordResetLimiter.TryRecord("code:" + email + ":" + role, Request.UserHostAddress))
            {
                var user = Db.Users.SingleOrDefault(u => u.Email == email && u.Role == role && !u.IsSuspended && !u.EmailVerifiedAtUtc.HasValue);
                if (user != null) new EmailCodeService(Db).Issue(user);
            }
            TempData["Notice"] = "Đã nhận yêu cầu gửi mã. Nếu hồ sơ cần xác minh và chưa vượt giới hạn gửi, mã mới sẽ được gửi; hãy kiểm tra cả thư rác.";
            return RedirectToAction("ConfirmRegistration", new { email, role });
        }
    }
}
