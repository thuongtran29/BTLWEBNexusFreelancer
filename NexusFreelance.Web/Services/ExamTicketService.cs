using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Security;

namespace NexusFreelance.Web.Services
{
    // Stateless, authenticated exam ticket. The browser cannot extend its deadline.
    // All IIS nodes must use the same configured machineKey in multi-server hosting.
    public static class ExamTicketService
    {
        public const int DurationSeconds = 20 * 60;

        public static string Issue(int userId, int trackId, DateTime startedAtUtc)
        {
            var issued = startedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            var payload = string.Join("|", userId.ToString(CultureInfo.InvariantCulture),
                trackId.ToString(CultureInfo.InvariantCulture), issued, Guid.NewGuid().ToString("N"));
            return HttpServerUtility.UrlTokenEncode(MachineKey.Protect(
                Encoding.UTF8.GetBytes(payload), "NexusFreelance", "SkillExam", "v1"));
        }

        public static DateTime Validate(string ticket, int userId, int trackId)
        {
            if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 2048)
                throw new InvalidOperationException("Phiên thi không hợp lệ. Vui lòng mở lại bài thi.");
            try
            {
                var protectedBytes = HttpServerUtility.UrlTokenDecode(ticket);
                if (protectedBytes == null) throw new CryptographicException();
                var unprotected = MachineKey.Unprotect(protectedBytes, "NexusFreelance", "SkillExam", "v1");
                if (unprotected == null) throw new CryptographicException();
                var parts = Encoding.UTF8.GetString(unprotected).Split('|');
                if (parts.Length != 4 || parts[3].Length != 32 ||
                    !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var owner) ||
                    !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var track) ||
                    !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                    owner != userId || track != trackId)
                    throw new CryptographicException();
                var startedAt = new DateTime(ticks, DateTimeKind.Utc);
                var elapsed = DateTime.UtcNow - startedAt;
                if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromSeconds(DurationSeconds))
                    throw new InvalidOperationException("Đã hết 20 phút làm bài. Vui lòng mở phiên thi mới.");
                return startedAt;
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException("Phiên thi không hợp lệ. Vui lòng mở lại bài thi.");
            }
            catch (CryptographicException)
            {
                throw new InvalidOperationException("Phiên thi không hợp lệ. Vui lòng mở lại bài thi.");
            }
        }
    }
}
