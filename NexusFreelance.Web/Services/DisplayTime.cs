using System;
using System.Globalization;

namespace NexusFreelance.Web.Services
{
    public static class DisplayTime
    {
        // Business timestamps are stored as UTC, including SQL datetime values with no Kind.
        public static string Vietnam(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(7)
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("vi-VN")) + " (UTC+7)";
    }
}
