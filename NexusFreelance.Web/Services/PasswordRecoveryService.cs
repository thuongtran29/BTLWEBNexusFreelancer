using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace NexusFreelance.Web.Services
{
    public static class PasswordRecoveryService
    {
        public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan EmailVerificationLifetime = TimeSpan.FromHours(24);

        // New deployments require verified email. Tests may explicitly opt out in an isolated process.
        public static bool RequireEmailVerification
        {
            get { return !string.Equals(Setting("NEXUS_REQUIRE_EMAIL_VERIFICATION"), "false", StringComparison.OrdinalIgnoreCase); }
        }

        public static string NewToken()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static bool HasValidFormat(string token)
        {
            if (token == null || token.Length != 43) return false;
            foreach (var c in token)
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                      (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
            return true;
        }

        public static string HashToken(string token)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.ASCII.GetBytes(token));
                return BitConverter.ToString(bytes).Replace("-", "");
            }
        }

        private static string Setting(string key)
        {
            return IntegrationSettings.Get(key).Trim();
        }

        public static bool IsDeliveryConfigured
        {
            get
            {
                var origin = Setting("NEXUS_PUBLIC_BASE_URL");
                int port;
                var mode=Setting("NEXUS_MAIL_MODE");
                if(string.Equals(mode,"pickup",StringComparison.OrdinalIgnoreCase))return IsLocalPickupConfigured;
                if(mode.Length>0 && !string.Equals(mode,"smtp",StringComparison.OrdinalIgnoreCase))return false;
                try { new MailAddress(Setting("NEXUS_SMTP_FROM")); } catch(FormatException){return false;} catch(ArgumentException){return false;}
                var host = Setting("NEXUS_SMTP_HOST");
                // Gmail does not support anonymous SMTP. Missing credentials must be
                // reported before registration creates an account or issues an OTP.
                if (string.Equals(host, "smtp.gmail.com", StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(Setting("NEXUS_SMTP_USERNAME")) ||
                     string.IsNullOrWhiteSpace(IntegrationSettings.Get("NEXUS_SMTP_PASSWORD")))) return false;
                return ValidOrigin(origin) &&
                       !string.IsNullOrEmpty(Setting("NEXUS_SMTP_HOST")) &&
                       int.TryParse(Setting("NEXUS_SMTP_PORT"), out port) && port > 0 && port <= 65535 &&
                       !string.IsNullOrEmpty(Setting("NEXUS_SMTP_FROM"));
            }
        }

        public static void SendResetEmail(string address, string token)
        {
            if (!IsDeliveryConfigured || !HasValidFormat(token))
                throw new InvalidOperationException("Password reset email is not configured.");

            var baseUrl = Setting("NEXUS_PUBLIC_BASE_URL").TrimEnd('/');
            var link = baseUrl + "/Account/ResetPassword?token=" + Uri.EscapeDataString(token);
            SendEmail(address, "NexusFreelance - Đặt lại mật khẩu",
                "Bạn vừa yêu cầu đặt lại mật khẩu NexusFreelance.\n" +
                "Mở liên kết sau trong vòng 30 phút (chỉ dùng một lần):\n" + link + "\n" +
                "Nếu bạn không yêu cầu, hãy bỏ qua thư này.");
        }

        public static void SendVerificationEmail(string address, string token)
        {
            if (!IsDeliveryConfigured || !HasValidFormat(token))
                throw new InvalidOperationException("Verification email is not configured.");
            var link = Setting("NEXUS_PUBLIC_BASE_URL").TrimEnd('/') +
                       "/Account/VerifyEmail?token=" + Uri.EscapeDataString(token);
            SendEmail(address, "NexusFreelance - Xác minh email",
                "Vui lòng xác minh địa chỉ email NexusFreelance trong 24 giờ bằng liên kết sau:\n" +
                link + "\nNếu bạn không đăng ký, hãy bỏ qua thư này.");
        }

        private static bool ValidOrigin(string origin)
        {
            Uri uri;
            return Uri.TryCreate(origin,UriKind.Absolute,out uri) && uri.Scheme=="https" && uri.AbsolutePath=="/" &&
                string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
        }
        public static bool IsLocalPickupConfigured => string.Equals(Setting("NEXUS_MAIL_MODE"), "pickup", StringComparison.OrdinalIgnoreCase) &&
            System.IO.Path.IsPathRooted(Setting("NEXUS_MAIL_PICKUP_DIRECTORY")) &&
            ValidOrigin(Setting("NEXUS_PUBLIC_BASE_URL"));

        public static void SendCode(string address, string code, string role)
        {
            SendEmail(address, "NexusFreelance - Mã xác nhận đăng ký", "Mã xác nhận cho vai trò " + role + ": " + code + "\nMã có hiệu lực 10 phút, tối đa 5 lần nhập. Không cung cấp mã cho người khác.");
        }

        public static bool PickupPathIsOutside(string directory,string webRoot)
        {
            var path=System.IO.Path.GetFullPath(directory).TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar);
            var root=System.IO.Path.GetFullPath(webRoot).TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar);
            return !path.Equals(root,StringComparison.OrdinalIgnoreCase) && !path.StartsWith(root+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        }

        private static void SendEmail(string address, string subject, string body)
        {
            if (!IsDeliveryConfigured) throw new InvalidOperationException("SMTP is not configured.");
            var port = IsLocalPickupConfigured ? 25 : int.Parse(Setting("NEXUS_SMTP_PORT"));
            using (var email = new MailMessage())
            {
                email.From = new MailAddress(IsLocalPickupConfigured ? "no-reply@nexus.local" : Setting("NEXUS_SMTP_FROM"));
                email.To.Add(new MailAddress(address));
                email.Subject = subject;
                email.SubjectEncoding = Encoding.UTF8;
                email.BodyEncoding = Encoding.UTF8;
                email.Body = body;
                using (var smtp = new SmtpClient(IsLocalPickupConfigured ? "localhost" : Setting("NEXUS_SMTP_HOST"), port))
                {
                    smtp.EnableSsl = true;
                    smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtp.Timeout = 10000;
                    var account = Setting("NEXUS_SMTP_USERNAME");
                    smtp.UseDefaultCredentials = false;
                    if (!string.IsNullOrEmpty(account))
                        smtp.Credentials = new NetworkCredential(account, IntegrationSettings.Get("NEXUS_SMTP_PASSWORD"));
                    if (IsLocalPickupConfigured)
                    {
                        var directory = System.IO.Path.GetFullPath(Setting("NEXUS_MAIL_PICKUP_DIRECTORY"));
                        var appRoot = System.Web.Hosting.HostingEnvironment.ApplicationPhysicalPath;
                        if (appRoot != null && !PickupPathIsOutside(directory,appRoot))
                            throw new InvalidOperationException("Mailbox must be outside the web root.");
                        System.IO.Directory.CreateDirectory(directory);
                        smtp.EnableSsl = false;
                        smtp.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
                        smtp.PickupDirectoryLocation = directory;
                    }
                    smtp.Send(email);
                }
            }
        }
    }

    // Best-effort development limiter; use a shared durable store for multi-worker hosting.
    public static class PasswordResetLimiter
    {
        private sealed class Counter { public DateTime FromUtc; public int Count; }
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Counter> Counts = new Dictionary<string, Counter>(StringComparer.Ordinal);
        private static readonly TimeSpan Window = TimeSpan.FromHours(1);

        public static bool TryRecord(string email, string address)
        {
            lock (Gate)
            {
                var now = DateTime.UtcNow;
                var accountKey = "email:" + email;
                var addressKey = "ip:" + (address ?? "unknown");
                var account = Get(accountKey, now);
                var ip = Get(addressKey, now);
                if (account.Count >= 3 || ip.Count >= 15) return false;
                account.Count++;
                ip.Count++;
                if (Counts.Count > 4096)
                {
                    var expired = new List<string>();
                    foreach (var item in Counts)
                        if (now - item.Value.FromUtc >= Window) expired.Add(item.Key);
                    foreach (var key in expired) Counts.Remove(key);
                }
                return true;
            }
        }

        private static Counter Get(string key, DateTime now)
        {
            Counter counter;
            if (Counts.TryGetValue(key, out counter) && now - counter.FromUtc < Window) return counter;
            return Counts[key] = new Counter { FromUtc = now };
        }
    }
}
