using System;
using System.Data;
using System.Linq;
using System.Net.Mail;
using System.Web.Mvc;
using System.Web.Security;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Controllers
{
    public partial class AccountController : BaseController
    {
        [HttpGet] public ActionResult Register() { return View(new RegisterForm()); }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Register(RegisterForm form)
        {
            if (form == null)
            {
                ModelState.AddModelError("", "Thiếu thông tin đăng ký.");
                return View(new RegisterForm());
            }
            if (form.Role != "Client" && form.Role != "Freelancer") ModelState.AddModelError("Role", "Vai trò không hợp lệ.");
            if (!ModelState.IsValid) return View(form);
            if (!PasswordResetLimiter.TryRecord("register:" + form.Email.Trim().ToLowerInvariant() + ":" + form.Role, Request.UserHostAddress))
            { Response.StatusCode = 429; ModelState.AddModelError("", "Đã có quá nhiều yêu cầu đăng ký. Vui lòng thử lại sau."); return View(form); }
            if (PasswordRecoveryService.RequireEmailVerification && !PasswordRecoveryService.IsDeliveryConfigured)
            {
                Response.StatusCode = 503;
                ModelState.AddModelError("", "Đăng ký đang tạm ngừng: hệ thống xác minh email chưa được cấu hình.");
                return View(form);
            }
            var email = form.Email.Trim().ToLowerInvariant();
            if (Db.Users.Any(u => u.Email == email && u.Role == form.Role))
            {
                ModelState.AddModelError("Email", "Email đã có hồ sơ cho vai trò này. Bạn có thể đăng ký vai trò còn lại.");
                return View(form);
            }
            var created = new UserAccount { Name = form.Name.Trim(), Email = email, Role = form.Role, PasswordHash = PasswordService.Hash(form.Password), JoinedAtUtc = DateTime.UtcNow };
            Db.Users.Add(created);
            try { Db.SaveChanges(); }
            catch (System.Data.Entity.Infrastructure.DbUpdateException)
            {
                ModelState.AddModelError("Email", "Không thể tạo tài khoản, hãy kiểm tra email.");
                return View(form);
            }
            if (PasswordRecoveryService.IsDeliveryConfigured)
            {
                if (!new EmailCodeService(Db).Issue(created))
                    TempData["Error"] = "Chưa gửi được mã xác minh. Tài khoản đang chờ xác nhận; hãy thử Gửi lại mã sau.";
                return RedirectToAction("ConfirmRegistration", new { email = created.Email, role = created.Role });
            }
            if (PasswordRecoveryService.RequireEmailVerification)
            {
                TempData["VerificationNotice"] = "Tài khoản đã được tạo. Hãy kiểm tra email và xác minh trước khi đăng nhập.";
                return RedirectToAction("Login");
            }
            AuthSessionService.SignIn(Response, created);
            return RedirectToAction("Index", "Dashboard");
        }

        [HttpGet] public ActionResult Login(string role = "Client") { return View(new LoginForm { Role = role=="Freelancer" || role=="Admin" ? role : "Client" }); }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Login(LoginForm form)
        {
            if (form == null)
            {
                ModelState.AddModelError("", "Thiếu thông tin đăng nhập.");
                return View(new LoginForm());
            }
            if (!ModelState.IsValid) return View(form);
            var email = form.Email.Trim().ToLowerInvariant();
            var clientAddress = Request.UserHostAddress;
            int retryAfter;
            if (LoginAttemptLimiter.IsBlocked(email, clientAddress, out retryAfter))
            {
                Response.StatusCode = 429;
                Response.TrySkipIisCustomErrors = true;
                Response.AppendHeader("Retry-After", retryAfter.ToString());
                ModelState.AddModelError("", "Quá nhiều lần đăng nhập sai. Vui lòng thử lại sau.");
                return View(form);
            }
            var candidates = Db.Users.Where(u => u.Email == email);
            if (!string.IsNullOrEmpty(form.Role)) candidates = candidates.Where(u => u.Role == form.Role);
            var accounts = candidates.Take(2).ToList();
            var user = accounts.Count == 1 ? accounts[0] : null;
            if (user == null || !PasswordService.Verify(form.Password, user.PasswordHash))
            {
                LoginAttemptLimiter.RecordFailure(email, clientAddress);
                ModelState.AddModelError("", "Email hoặc mật khẩu không đúng.");
                return View(form);
            }
            if (user.IsSuspended)
            {
                ModelState.AddModelError("", "Tài khoản đã bị tạm khóa. Vui lòng liên hệ quản trị viên.");
                return View(form);
            }
            if (PasswordRecoveryService.RequireEmailVerification && !user.EmailVerifiedAtUtc.HasValue)
            {
                if(user.Role!="Admin") {
                    TempData["Notice"]="Email chưa được xác minh. Nhập mã đã nhận hoặc yêu cầu gửi lại mã bên dưới.";
                    return RedirectToAction("ConfirmRegistration",new{email=user.Email,role=user.Role});
                }
                ModelState.AddModelError("", "Vui lòng xác minh email trước khi đăng nhập. Bạn có thể yêu cầu gửi lại liên kết.");return View(form);
            }
            // Re-read under the same user lock as reset. A login which verified an old
            // hash must never overwrite a password reset completed while PBKDF2 ran.
            var verifiedHash = user.PasswordHash;
            using (var tx = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                Db.Database.SqlQuery<int>("SELECT Id FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", user.Id).Single();
                Db.Entry(user).Reload();
                if (user.IsSuspended || (!string.IsNullOrEmpty(form.Role) && user.Role != form.Role) ||
                    (PasswordRecoveryService.RequireEmailVerification && !user.EmailVerifiedAtUtc.HasValue) ||
                    (user.PasswordHash != verifiedHash && !PasswordService.Verify(form.Password, user.PasswordHash)))
                { ModelState.AddModelError("", "Thông tin đăng nhập đã thay đổi. Vui lòng thử lại."); return View(form); }
                if (PasswordService.NeedsRehash(user.PasswordHash)) { user.PasswordHash = PasswordService.Hash(form.Password); Db.SaveChanges(); }
                tx.Commit();
            }
            LoginAttemptLimiter.RecordSuccess(email);
            AuthSessionService.SignIn(Response, user);
            return RedirectToAction("Index", user.Role == "Admin" ? "Admin" : "Dashboard");
        }

        [HttpGet, AllowAnonymous]
        public ActionResult ResendVerification()
        {
            return View(new ForgotPasswordForm());
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public ActionResult ResendVerification(ForgotPasswordForm form)
        {
            if (form == null || !ModelState.IsValid)
                return View(form ?? new ForgotPasswordForm());
            if(form.Role=="Client" || form.Role=="Freelancer")return ResendRegistrationCode(form.Email,form.Role);
            if(form.Role!="Admin"){ModelState.AddModelError("Role","Hãy chọn vai trò cần xác minh.");return View(form);}
            var email = form.Email.Trim().ToLowerInvariant();
            // Do not reveal whether an address exists, is verified, or is suspended.
            if (PasswordRecoveryService.IsDeliveryConfigured &&
                PasswordResetLimiter.TryRecord("verify:" + email, Request.UserHostAddress))
            {
                var users = Db.Users.Where(u => u.Email == email && (form.Role == null || u.Role == form.Role) && !u.IsSuspended && !u.EmailVerifiedAtUtc.HasValue).OrderBy(u => u.Id).Take(2).ToList();
                var user = users.Count == 1 ? users[0] : null;
                if (user != null) IssueVerificationEmail(user);
            }
            ViewBag.RequestAccepted = true;
            return View(new ForgotPasswordForm());
        }

        [HttpGet, AllowAnonymous]
        public ActionResult VerifyEmail(string token)
        {
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            Response.AppendHeader("Referrer-Policy", "no-referrer");
            var valid = false;
            if (PasswordRecoveryService.HasValidFormat(token))
            {
                var hash = PasswordRecoveryService.HashToken(token);
                var now = DateTime.UtcNow;
                valid = Db.EmailVerificationTokens.Any(t => t.TokenHash == hash &&
                    !t.ConsumedAtUtc.HasValue && t.ExpiresAtUtc > now &&
                    !t.User.IsSuspended && !t.User.EmailVerifiedAtUtc.HasValue);
            }
            ViewBag.InvalidToken = !valid;
            return View(new VerifyEmailForm { Token = valid ? token : "" });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public ActionResult VerifyEmail(VerifyEmailForm form)
        {
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            Response.AppendHeader("Referrer-Policy", "no-referrer");
            if (form == null || !ModelState.IsValid) return View(form ?? new VerifyEmailForm());
            if (!PasswordRecoveryService.HasValidFormat(form.Token))
            {
                ModelState.AddModelError("", "Liên kết xác minh không hợp lệ hoặc đã hết hạn.");
                return View(form);
            }

            var hash = PasswordRecoveryService.HashToken(form.Token);
            var ownerId = Db.EmailVerificationTokens.AsNoTracking()
                .Where(t => t.TokenHash == hash).Select(t => (int?)t.UserAccountId).SingleOrDefault();
            if (!ownerId.HasValue)
            {
                ModelState.AddModelError("", "Liên kết xác minh không hợp lệ hoặc đã hết hạn.");
                return View(form);
            }
            using (var transaction = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                // Consistent user -> token locking serializes competing verification requests.
                var user = Db.Users.SqlQuery("SELECT * FROM dbo.Users WITH (UPDLOCK,HOLDLOCK) WHERE Id=@p0", ownerId.Value)
                    .SingleOrDefault();
                var token = Db.EmailVerificationTokens.SqlQuery(
                    "SELECT * FROM dbo.EmailVerificationTokens WITH (UPDLOCK,HOLDLOCK) WHERE TokenHash=@p0", hash)
                    .SingleOrDefault();
                var now = DateTime.UtcNow;
                if (user == null || token == null || token.UserAccountId != user.Id ||
                    token.ConsumedAtUtc.HasValue || token.ExpiresAtUtc <= now ||
                    user.IsSuspended || user.EmailVerifiedAtUtc.HasValue)
                {
                    ModelState.AddModelError("", "Liên kết xác minh không hợp lệ hoặc đã hết hạn.");
                    return View(form);
                }
                user.EmailVerifiedAtUtc = now;
                var remaining = Db.EmailVerificationTokens
                    .Where(t => t.UserAccountId == user.Id && !t.ConsumedAtUtc.HasValue).ToList();
                foreach (var entry in remaining) entry.ConsumedAtUtc = now;
                Db.SaveChanges();
                transaction.Commit();
            }
            TempData["VerificationNotice"] = "Email đã được xác minh. Bạn có thể đăng nhập.";
            return RedirectToAction("Login");
        }

        private void IssueVerificationEmail(UserAccount user)
        {
            var now = DateTime.UtcNow;
            var token = PasswordRecoveryService.NewToken();
            var record = new EmailVerificationToken
            {
                UserAccountId = user.Id,
                TokenHash = PasswordRecoveryService.HashToken(token),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(PasswordRecoveryService.EmailVerificationLifetime)
            };
            Db.EmailVerificationTokens.Add(record);
            Db.SaveChanges();
            try { PasswordRecoveryService.SendVerificationEmail(user.Email, token); }
            catch (Exception exception) when (exception is SmtpException || exception is InvalidOperationException ||
                                               exception is FormatException || exception is ArgumentException || exception is System.IO.IOException || exception is UnauthorizedAccessException)
            {
                record.ConsumedAtUtc = DateTime.UtcNow;
                Db.SaveChanges();
                System.Diagnostics.Trace.TraceWarning("Email verification delivery failed.");
            }
        }

        [HttpGet, AllowAnonymous]
        public ActionResult ForgotPassword(string role = "Client")
        {
            return View(new ForgotPasswordForm { Role = role == "Freelancer" || role == "Admin" ? role : "Client" });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public ActionResult ForgotPassword(ForgotPasswordForm form)
        {
            if (form == null || (form.Role != "Client" && form.Role != "Freelancer" && form.Role != "Admin"))
                ModelState.AddModelError("Role", "Vui lòng chọn đúng vai trò cần khôi phục.");
            if (form == null || !ModelState.IsValid)
                return View(form ?? new ForgotPasswordForm());

            var email = form.Email.Trim().ToLowerInvariant();
            // Global delivery/rate errors are checked before lookup, so they disclose no account membership.
            if (!PasswordRecoveryService.IsDeliveryConfigured)
            {
                Response.StatusCode = 503; Response.TrySkipIisCustomErrors = true;
                ModelState.AddModelError("", "Dịch vụ gửi email hiện chưa sẵn sàng. Vui lòng thử lại sau hoặc liên hệ quản trị.");
                return View(form);
            }
            if (!PasswordResetLimiter.TryRecord(email, Request.UserHostAddress))
            {
                Response.StatusCode = 429; Response.TrySkipIisCustomErrors = true;
                ModelState.AddModelError("", "Bạn đã gửi quá nhiều yêu cầu. Vui lòng kiểm tra thư đã nhận hoặc thử lại sau một giờ.");
                return View(form);
            }
            // Identical acknowledgement for present/absent/suspended accounts and delivery failure.
            {
                var users = Db.Users.Where(u => u.Email == email && u.Role == form.Role && !u.IsSuspended).OrderBy(u => u.Id).Take(2).ToList();
                var user = users.Count == 1 ? users[0] : null;
                if (user != null)
                {
                    var now = DateTime.UtcNow;
                    var token = PasswordRecoveryService.NewToken();
                    var record = new PasswordResetToken
                    {
                        UserAccountId = user.Id,
                        TokenHash = PasswordRecoveryService.HashToken(token),
                        CreatedAtUtc = now,
                        ExpiresAtUtc = now.Add(PasswordRecoveryService.TokenLifetime)
                    };
                    Db.PasswordResetTokens.Add(record);
                    Db.SaveChanges();
                    try { PasswordRecoveryService.SendResetEmail(email, token); }
                    catch (Exception exception) when (exception is SmtpException || exception is InvalidOperationException ||
                                                       exception is FormatException || exception is ArgumentException || exception is System.IO.IOException || exception is UnauthorizedAccessException)
                    {
                        // A failed delivery must not leave a valid reset token behind.
                        record.ConsumedAtUtc = DateTime.UtcNow;
                        Db.SaveChanges();
                        System.Diagnostics.Trace.TraceWarning("Password reset email delivery failed.");
                    }
                }
            }
            ViewBag.RequestAccepted = true;
            return View(new ForgotPasswordForm());
        }

        [HttpGet, AllowAnonymous]
        public ActionResult ResetPassword(string token)
        {
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            Response.AppendHeader("Referrer-Policy", "no-referrer");
            var valid = false;
            if (PasswordRecoveryService.HasValidFormat(token))
            {
                var hash = PasswordRecoveryService.HashToken(token);
                var now = DateTime.UtcNow;
                valid = Db.PasswordResetTokens.Any(t => t.TokenHash == hash &&
                    !t.ConsumedAtUtc.HasValue && t.ExpiresAtUtc > now && !t.User.IsSuspended &&
                    (!t.User.PasswordChangedAtUtc.HasValue || t.CreatedAtUtc > t.User.PasswordChangedAtUtc.Value));
            }
            ViewBag.InvalidToken = !valid;
            return View(new ResetPasswordForm { Token = valid ? token : "" });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public ActionResult ResetPassword(ResetPasswordForm form)
        {
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            Response.AppendHeader("Referrer-Policy", "no-referrer");
            if (form == null || !ModelState.IsValid) return View(form ?? new ResetPasswordForm());
            if (!PasswordRecoveryService.HasValidFormat(form.Token))
            {
                ModelState.AddModelError("", "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
                return View(form);
            }

            var hash = PasswordRecoveryService.HashToken(form.Token);
            // Read the owner first without a write lock. Concurrent resets for distinct
            // tokens belonging to one account must acquire locks in the same order:
            // user -> token -> remaining tokens. The token is revalidated after locking.
            var ownerId = Db.PasswordResetTokens.AsNoTracking()
                .Where(t => t.TokenHash == hash).Select(t => (int?)t.UserAccountId)
                .SingleOrDefault();
            if (!ownerId.HasValue)
            {
                ModelState.AddModelError("", "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
                return View(form);
            }
            using (var transaction = Db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var user = Db.Users.SqlQuery("SELECT * FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE Id = @p0", ownerId.Value)
                    .SingleOrDefault();
                var reset = Db.PasswordResetTokens.SqlQuery(
                    "SELECT * FROM dbo.PasswordResetTokens WITH (UPDLOCK, HOLDLOCK) WHERE TokenHash = @p0", hash)
                    .SingleOrDefault();
                var now = DateTime.UtcNow;
                if (user == null || reset == null || reset.UserAccountId != user.Id ||
                    reset.ConsumedAtUtc.HasValue || reset.ExpiresAtUtc <= now)
                {
                    ModelState.AddModelError("", "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
                    return View(form);
                }
                if (user.IsSuspended ||
                    (user.PasswordChangedAtUtc.HasValue && reset.CreatedAtUtc <= user.PasswordChangedAtUtc.Value))
                {
                    ModelState.AddModelError("", "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
                    return View(form);
                }
                user.PasswordHash = PasswordService.Hash(form.Password);
                user.PasswordChangedAtUtc = now;
                var outstanding = Db.PasswordResetTokens
                    .Where(t => t.UserAccountId == user.Id && !t.ConsumedAtUtc.HasValue).ToList();
                foreach (var item in outstanding) item.ConsumedAtUtc = now;
                Db.SaveChanges();
                transaction.Commit();
            }
            FormsAuthentication.SignOut();
            TempData["PasswordResetSuccess"] = "Mật khẩu đã được đổi. Vui lòng đăng nhập lại.";
            return RedirectToAction("Login");
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult Logout()
        {
            FormsAuthentication.SignOut();
            return RedirectToAction("Index", "Home");
        }
    }
}
