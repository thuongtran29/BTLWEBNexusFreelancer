using System;
using System.Web;
using System.Web.Security;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Services
{
    public static class AuthSessionService
    {
        private static string Revision(UserAccount user)
        {
            return user.PasswordChangedAtUtc.HasValue
                ? user.PasswordChangedAtUtc.Value.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "";
        }

        public static void SignIn(HttpResponseBase response, UserAccount user)
        {
            var now = DateTime.Now;
            var ticket = new FormsAuthenticationTicket(2, user.Id + ":" + user.Email, now,
                now.Add(FormsAuthentication.Timeout), false, user.Id + "|" + Revision(user), FormsAuthentication.FormsCookiePath);
            response.Cookies.Add(new HttpCookie(FormsAuthentication.FormsCookieName, FormsAuthentication.Encrypt(ticket))
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax,
                Path = FormsAuthentication.FormsCookiePath
            });
        }

        public static int? AccountId(HttpRequestBase request)
        {
            try { var cookie = request.Cookies[FormsAuthentication.FormsCookieName];
                var ticket = cookie == null ? null : FormsAuthentication.Decrypt(cookie.Value);
                int id; return ticket != null && !ticket.Expired && int.TryParse(ticket.UserData.Split('|')[0], out id) ? (int?)id : null;
            } catch (System.Security.Cryptography.CryptographicException) { return null; } catch (ArgumentException) { return null; }
        }

        public static bool IsCurrent(HttpRequestBase request, UserAccount user)
        {
            var cookie = request.Cookies[FormsAuthentication.FormsCookieName];
            if (cookie == null || string.IsNullOrEmpty(cookie.Value)) return false;
            try
            {
                var ticket = FormsAuthentication.Decrypt(cookie.Value);
                return ticket != null && !ticket.Expired &&
                       string.Equals(ticket.Name, user.Id + ":" + user.Email, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(ticket.UserData, user.Id + "|" + Revision(user), StringComparison.Ordinal);
            }
            catch (System.Security.Cryptography.CryptographicException) { return false; }
            catch (ArgumentException) { return false; }
        }
    }
}
