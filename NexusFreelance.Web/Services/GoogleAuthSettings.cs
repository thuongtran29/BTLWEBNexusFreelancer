using System;
namespace NexusFreelance.Web.Services
{
    public static class GoogleAuthSettings
    {
        public static bool IsConfigured
        {
            get {
                Uri callback;
                return !string.IsNullOrWhiteSpace(IntegrationSettings.Get("NEXUS_GOOGLE_CLIENT_ID")) &&
                    !string.IsNullOrWhiteSpace(IntegrationSettings.Get("NEXUS_GOOGLE_CLIENT_SECRET")) &&
                    Uri.TryCreate(IntegrationSettings.Get("NEXUS_GOOGLE_REDIRECT_URI").Trim(),UriKind.Absolute,out callback) &&
                    callback.Scheme=="https" && callback.AbsolutePath=="/Account/GoogleCallback" &&
                    string.IsNullOrEmpty(callback.UserInfo) && string.IsNullOrEmpty(callback.Query) && string.IsNullOrEmpty(callback.Fragment);
            }
        }
    }
}
