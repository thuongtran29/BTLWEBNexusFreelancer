using System;
using System.Globalization;

namespace NexusFreelance.Web.Services
{
    public static class ActivityFilters
    {
        // Calendar dates are UTC; the upper bound is exclusive to include the whole final day.
        public static bool TryDates(string from, string to, out DateTime? start, out DateTime? end)
        {
            start = end = null;
            DateTime date;
            if (!string.IsNullOrEmpty(from))
            {
                if (!DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out date)) return false;
                start = DateTime.SpecifyKind(date, DateTimeKind.Utc);
            }
            if (!string.IsNullOrEmpty(to))
            {
                if (!DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out date) || date.Date == DateTime.MaxValue.Date) return false;
                end = DateTime.SpecifyKind(date.AddDays(1), DateTimeKind.Utc);
            }
            return !start.HasValue || !end.HasValue || start.Value < end.Value;
        }

        public static bool SafeExternalUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps &&
                !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
        }
    }
}
