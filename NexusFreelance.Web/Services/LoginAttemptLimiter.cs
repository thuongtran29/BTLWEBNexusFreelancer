using System;
using System.Collections.Generic;

namespace NexusFreelance.Web.Services
{
    // Local development throttle. A shared store is required for multi-worker deployment.
    public static class LoginAttemptLimiter
    {
        private sealed class AttemptWindow
        {
            public DateTime StartedUtc;
            public int Failures;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, AttemptWindow> Attempts =
            new Dictionary<string, AttemptWindow>(StringComparer.Ordinal);
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
        private const int AccountFailureLimit = 5;
        private const int AddressFailureLimit = 20;

        private static string AccountKey(string normalizedEmail) { return "account:" + normalizedEmail; }
        private static string AddressKey(string address) { return "address:" + (address ?? "unknown"); }

        private static AttemptWindow Current(string key, DateTime now)
        {
            AttemptWindow value;
            if (!Attempts.TryGetValue(key, out value)) return null;
            if (now - value.StartedUtc < Window) return value;
            Attempts.Remove(key);
            return null;
        }

        public static bool IsBlocked(string normalizedEmail, string address, out int retryAfterSeconds)
        {
            lock (Sync)
            {
                var now = DateTime.UtcNow;
                var account = Current(AccountKey(normalizedEmail), now);
                var ip = Current(AddressKey(address), now);
                var accountBlocked = account != null && account.Failures >= AccountFailureLimit;
                var addressBlocked = ip != null && ip.Failures >= AddressFailureLimit;
                retryAfterSeconds = 0;
                if (!accountBlocked && !addressBlocked) return false;

                var left = TimeSpan.Zero;
                if (accountBlocked) left = Window - (now - account.StartedUtc);
                if (addressBlocked)
                {
                    var ipLeft = Window - (now - ip.StartedUtc);
                    if (ipLeft > left) left = ipLeft;
                }
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(left.TotalSeconds));
                return true;
            }
        }

        public static void RecordFailure(string normalizedEmail, string address)
        {
            lock (Sync)
            {
                var now = DateTime.UtcNow;
                Increment(AccountKey(normalizedEmail), now);
                Increment(AddressKey(address), now);
                if (Attempts.Count > 4096)
                {
                    var expired = new List<string>();
                    foreach (var item in Attempts)
                        if (now - item.Value.StartedUtc >= Window) expired.Add(item.Key);
                    foreach (var key in expired) Attempts.Remove(key);
                }
            }
        }

        private static void Increment(string key, DateTime now)
        {
            var value = Current(key, now);
            if (value == null)
                Attempts[key] = new AttemptWindow { StartedUtc = now, Failures = 1 };
            else
                value.Failures++;
        }

        public static void RecordSuccess(string normalizedEmail)
        {
            lock (Sync) Attempts.Remove(AccountKey(normalizedEmail));
        }
    }
}
