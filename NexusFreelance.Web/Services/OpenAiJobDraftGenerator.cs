using System;
using System.Net.Http;
using System.Text.RegularExpressions;
namespace NexusFreelance.Web.Services
{
    public static class JobDraftGeneratorFactory
    {
        // Empty preserves the existing OpenAI installation. No automatic provider fallback.
        public static string SelectedProvider {
            get {
                var selected = IntegrationSettings.Get("NEXUS_AI_PROVIDER");
                if (string.IsNullOrWhiteSpace(selected) || selected.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)) return "OpenAI";
                return selected.Equals("Gemini", StringComparison.OrdinalIgnoreCase) ? "Gemini" : "";
            }
        }
        public static bool IsExternalOrigin(string origin) =>
            !string.IsNullOrEmpty(origin) && (origin.StartsWith("OpenAI-", StringComparison.Ordinal) || origin.StartsWith("Gemini-", StringComparison.Ordinal));
        public static bool IsExternalAiConfigured {
            get {
                if (!string.Equals(IntegrationSettings.Get("NEXUS_AI_ENABLED"), "true", StringComparison.OrdinalIgnoreCase)) return false;
                var provider = SelectedProvider; if (provider.Length == 0) return false;
                var key = IntegrationSettings.Get(provider == "Gemini" ? "NEXUS_GEMINI_API_KEY" : "NEXUS_OPENAI_API_KEY");
                var model = IntegrationSettings.Get(provider == "Gemini" ? "NEXUS_GEMINI_MODEL" : "NEXUS_OPENAI_MODEL");
                return !string.IsNullOrWhiteSpace(key) && Regex.IsMatch(model ?? "", provider == "Gemini" ? @"\Agemini-[A-Za-z0-9._-]{1,26}\z" : @"\A[A-Za-z0-9._:-]{1,33}\z");
            }
        }
        public static IJobDraftGenerator Create(bool clientOptedIn = false, string expectedProvider = null) {
            var fallback = new RuleBasedJobDraftGenerator();
            if (!clientOptedIn || !IsExternalAiConfigured) return fallback;
            var provider = SelectedProvider;
            if (expectedProvider != null && !string.Equals(provider, expectedProvider, StringComparison.Ordinal)) throw new InvalidOperationException("Nhà cung cấp AI đã thay đổi. Hãy tải lại trang và xác nhận trước khi gửi dữ liệu.");
            if (provider == "Gemini") return new GeminiJobDraftGenerator(IntegrationSettings.Get("NEXUS_GEMINI_API_KEY"), IntegrationSettings.Get("NEXUS_GEMINI_MODEL"), fallback);
            return new OpenAiJobDraftGenerator(IntegrationSettings.Get("NEXUS_OPENAI_API_KEY"), IntegrationSettings.Get("NEXUS_OPENAI_MODEL"), fallback);
        }
    }
    public sealed class OpenAiJobDraftGenerator : ChatCompletionJobDraftGenerator
    {
        public OpenAiJobDraftGenerator(string key, string model, IJobDraftGenerator fallback) : this(key, model, fallback, null) { }
        public OpenAiJobDraftGenerator(string key, string model, IJobDraftGenerator fallback, HttpClient client)
            : base(key, model, fallback, client, "OpenAI", "https://api.openai.com/v1/chat/completions") { }
    }
}
