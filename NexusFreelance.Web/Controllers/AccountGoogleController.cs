using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.Script.Serialization;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;

namespace NexusFreelance.Web.Controllers
{
    public partial class AccountController
    {
        private static readonly HttpClient GoogleHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        private static string GoogleSetting(string name) => IntegrationSettings.Get(name).Trim();
        private static bool GoogleConfigured() => GoogleAuthSettings.IsConfigured;

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public ActionResult GoogleLogin(string role, bool link = false)
        {
            if (role != "Client" && role != "Freelancer") return new HttpStatusCodeResult(400);
            if (link && (CurrentUser == null || CurrentUser.Role != role)) return Forbidden();
            if (!GoogleConfigured()) { TempData["Error"] = "Đăng nhập Google chưa sẵn sàng. Bạn vẫn có thể dùng email và mật khẩu."; return RedirectToAction("Login",new{role}); }
            var state = PasswordRecoveryService.NewToken(); var verifier = PasswordRecoveryService.NewToken(); string challenge;
            using (var sha = SHA256.Create()) challenge = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Session["GoogleState"] = state; Session["GoogleVerifier"] = verifier; Session["GoogleRole"] = role;
            Session["GoogleExpires"] = DateTime.UtcNow.AddMinutes(5); Session["GoogleLinkId"] = link ? (int?)CurrentUser.Id : null;
            return Redirect("https://accounts.google.com/o/oauth2/v2/auth?response_type=code&scope=openid%20email%20profile&prompt=select_account&client_id=" +
                Uri.EscapeDataString(GoogleSetting("NEXUS_GOOGLE_CLIENT_ID")) + "&redirect_uri=" + Uri.EscapeDataString(GoogleSetting("NEXUS_GOOGLE_REDIRECT_URI")) +
                "&state=" + state + "&code_challenge=" + challenge + "&code_challenge_method=S256");
        }

        [HttpGet, AllowAnonymous]
        public async Task<ActionResult> GoogleCallback(string code, string state, string error)
        {
            var expected = Session["GoogleState"] as string; var verifier = Session["GoogleVerifier"] as string;
            var role = Session["GoogleRole"] as string; var expires = Session["GoogleExpires"] as DateTime?; var linkId = Session["GoogleLinkId"] as int?;
            foreach (var key in new[] { "GoogleState", "GoogleVerifier", "GoogleRole", "GoogleExpires", "GoogleLinkId" }) Session.Remove(key);
            Response.AppendHeader("Referrer-Policy", "no-referrer"); Response.Cache.SetNoStore();
            if (!string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(expected) && expected==state && expires.HasValue && expires.Value>DateTime.UtcNow)
            { TempData["Error"]="Bạn đã hủy hoặc Google chưa cho phép đăng nhập. Hãy thử lại hoặc dùng email và mật khẩu."; return RedirectToAction("Login",new{role}); }
            if (!GoogleConfigured() || string.IsNullOrEmpty(expected) || !string.Equals(expected, state, StringComparison.Ordinal) ||
                !expires.HasValue || expires.Value <= DateTime.UtcNow || string.IsNullOrEmpty(verifier) ||
                (role != "Client" && role != "Freelancer") || string.IsNullOrEmpty(code) || code.Length > 4096 || !string.IsNullOrEmpty(error))
                return new HttpStatusCodeResult(400, "Phiên Google không hợp lệ hoặc đã hết hạn.");
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 65536 };
                // Fixed HTTPS endpoints only. Authorization code and access token never reach logs or the browser.
                using (var tokenResponse = await GoogleHttp.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string,string> {
                    {"code", code}, {"client_id", GoogleSetting("NEXUS_GOOGLE_CLIENT_ID")}, {"client_secret", GoogleSetting("NEXUS_GOOGLE_CLIENT_SECRET")},
                    {"redirect_uri", GoogleSetting("NEXUS_GOOGLE_REDIRECT_URI")}, {"grant_type", "authorization_code"}, {"code_verifier", verifier} })))
                {
                    tokenResponse.EnsureSuccessStatusCode();
                    var token = serializer.Deserialize<Dictionary<string,object>>(await tokenResponse.Content.ReadAsStringAsync());
                    var accessToken = token!=null && token.ContainsKey("access_token") ? token["access_token"] as string : null;
                    if (string.IsNullOrEmpty(accessToken) || accessToken.Length > 8192) throw new InvalidOperationException();
                    using (var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo"))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                        using (var response = await GoogleHttp.SendAsync(request))
                        {
                            response.EnsureSuccessStatusCode();
                            var identity = serializer.Deserialize<Dictionary<string,object>>(await response.Content.ReadAsStringAsync());
                            if(identity==null)throw new InvalidOperationException();
                            var email = identity.ContainsKey("email") ? identity["email"] as string : null;
                            var subject = identity.ContainsKey("sub") ? identity["sub"] as string : null;
                            if (string.IsNullOrWhiteSpace(email) || email.Length > 200 || string.IsNullOrWhiteSpace(subject) || subject.Length > 200 ||
                                !identity.ContainsKey("email_verified") || !(identity["email_verified"] is bool) || !(bool)identity["email_verified"])
                                throw new InvalidOperationException();
                            email = email.Trim().ToLowerInvariant();
                            var user = Db.Users.SingleOrDefault(u => u.GoogleSubject == subject && u.Role == role);
                            if (linkId.HasValue)
                            {
                                if (CurrentUser == null || CurrentUser.Id != linkId.Value || CurrentUser.Role != role || CurrentUser.Email != email ||
                                    (user != null && user.Id != CurrentUser.Id) || (CurrentUser.GoogleSubject != null && CurrentUser.GoogleSubject != subject)) return Forbidden();
                                user = CurrentUser; user.GoogleSubject = subject; user.EmailVerifiedAtUtc = user.EmailVerifiedAtUtc ?? DateTime.UtcNow;
                            }
                            else if (user == null)
                            {
                                if (Db.Users.Any(u => u.Email == email && u.Role == role))
                                { TempData["Error"] = "Hồ sơ này đã tồn tại. Hãy đăng nhập bằng mật khẩu rồi liên kết Google trong mục Tài khoản."; return RedirectToAction("Login"); }
                                var name = identity.ContainsKey("name") ? identity["name"] as string : null;
                                name = string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name.Trim();
                                user = new UserAccount { Email = email, Name = name.Substring(0, Math.Min(120, name.Length)), Role = role, GoogleSubject = subject,
                                    PasswordHash = PasswordService.Hash(PasswordRecoveryService.NewToken()), EmailVerifiedAtUtc = DateTime.UtcNow, JoinedAtUtc = DateTime.UtcNow };
                                Db.Users.Add(user);
                            }
                            if (user.IsSuspended) return Forbidden();
                            Db.SaveChanges(); AuthSessionService.SignIn(Response, user); return RedirectToAction("Index", "Dashboard");
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException || ex is ArgumentException || ex is System.Data.Entity.Infrastructure.DbUpdateException)
            { TempData["Error"] = "Không hoàn tất được đăng nhập Google. Vui lòng thử lại hoặc dùng email và mật khẩu."; return RedirectToAction("Login"); }
        }

        [Authorize, HttpGet]
        public ActionResult Security() { return View(CurrentUser); }
    }
}
