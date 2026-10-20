using System.Net.Http;
namespace NexusFreelance.Web.Services
{
    // Fixed Google compatibility endpoint; shares prompt, JSON contract and validation with OpenAI.
    public sealed class GeminiJobDraftGenerator : ChatCompletionJobDraftGenerator
    {
        public GeminiJobDraftGenerator(string key, string model, IJobDraftGenerator fallback) : this(key, model, fallback, null) { }
        public GeminiJobDraftGenerator(string key, string model, IJobDraftGenerator fallback, HttpClient client)
            : base(key, model, fallback, client, "Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions") { }
    }
}
