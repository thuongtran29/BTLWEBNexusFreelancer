using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Services
{
    public interface IExternalJobDraftGenerator : IJobDraftGenerator { string FailureMessage { get; } }
    internal sealed class JobDraftProviderException : InvalidOperationException { public JobDraftProviderException(string message) : base(message) { } }
    // Optional provider. A failed or invalid model response cannot create an invalid Job Studio version.
    public abstract class ChatCompletionJobDraftGenerator : IExternalJobDraftGenerator
    {
        private static readonly HttpClient SharedClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(90) };
        private readonly string apiKey;
        private readonly string model;
        private readonly IJobDraftGenerator fallback;
        private readonly HttpClient client;

        private readonly Uri endpoint;
        private readonly string provider;
        protected ChatCompletionJobDraftGenerator(string apiKey, string model, IJobDraftGenerator fallback, HttpClient client, string provider, string endpoint)
        {
            this.apiKey = apiKey; this.model = model; this.provider = provider;
            this.fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            this.client = client ?? SharedClient; this.endpoint = new Uri(endpoint);
        }
        public string FailureMessage { get; private set; }

        public string GeneratorName { get; private set; } = "RuleBasedDemo-v2";

        public JobEditForm Generate(JobIdeaForm idea)
        {
            GeneratorName = fallback.GeneratorName;
            FailureMessage = null;
            try
            {
                var generated = RequestDraft(idea);
                if (!Valid(generated, idea.ProjectType))
                    throw new InvalidOperationException("AI draft did not pass project validation.");
                GeneratorName = provider + "-" + model;
                return generated;
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException) &&
                !(ex is System.Threading.ThreadAbortException))
            {
                // No raw API response, Client idea, authentication header or error body in logs.
                Trace.TraceWarning("AI Job Studio provider unavailable; generated content will not be saved as external AI.");
                FailureMessage = ex is TaskCanceledException
                    ? provider + " chưa trả kết quả trong 90 giây. Chưa lưu bản nháp; hãy thử lại sau."
                    : ex is JobDraftProviderException ? ex.Message
                    : provider + " chưa trả về JD hợp lệ. Chưa lưu bản nháp. Hãy thử lại sau hoặc chủ động dùng mẫu nội bộ.";
                return fallback.Generate(idea);
            }
        }

        private JobEditForm RequestDraft(JobIdeaForm idea)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = 22000 };
            var requestObject = new Dictionary<string, object> {
                { "model", model },
                { provider == "Gemini" ? "max_tokens" : "max_completion_tokens", 6000 },
                { "response_format", new { type = "json_schema", json_schema = new { name = "freelance_job_brief", strict = true, schema = OutputSchema(provider == "Gemini", idea.ProjectType) } } },
                { "messages", new object[] { new { role = "system", content = JobPrompt.System }, new { role = "user", content = UserMessage(idea) } } }
            };
            if (provider == "OpenAI") requestObject["store"] = false;
            else requestObject["reasoning_effort"] = "low"; // Bound Gemini thinking latency for the interactive JD form.
            var body = json.Serialize(requestObject);
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90)))
            {
                using (var response = SendDraftRequest(body, deadline.Token))
                {
                    if (!response.IsSuccessStatusCode) {
                        var status = (int)response.StatusCode;
                        var explanation = status == 429 ? "đã hết hạn mức hoặc đang giới hạn lượt gọi. Kiểm tra quota Free Tier và thử lại sau"
                            : status == 401 || status == 403 ? "chưa chấp nhận key hoặc quyền truy cập. Kiểm tra key và project Google/OpenAI"
                            : status == 404 ? "không tìm thấy model đã cấu hình hoặc project chưa có quyền dùng model này"
                            : provider == "Gemini" && (status == 502 || status == 503 || status == 504) ? "tạm quá tải hoặc chưa sẵn sàng (" + status + "). Đã thử lại có giới hạn; hãy thử lại sau"
                            : "đang gặp lỗi kết nối dịch vụ. Hãy thử lại sau";
                        throw new JobDraftProviderException(provider + " " + explanation + ". Chưa lưu bản nháp.");
                    }
                    // Bound deserialization regardless of the provider's response size.
                    if (response.Content.Headers.ContentLength.GetValueOrDefault() > 60000)
                        throw new InvalidOperationException("AI response too large.");
                    string payload;
                    using (var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    using (var bounded = new MemoryStream()) {
                        var buffer = new byte[4096]; int read;
                        while ((read = stream.ReadAsync(buffer, 0, buffer.Length, deadline.Token).GetAwaiter().GetResult()) > 0) {
                            if (bounded.Length + read > 60000) throw new InvalidOperationException("AI response too large.");
                            bounded.Write(buffer, 0, read);
                        }
                        payload = Encoding.UTF8.GetString(bounded.ToArray());
                    }
                    var envelope = json.DeserializeObject(payload) as Dictionary<string, object>;
                    if (envelope == null || !envelope.ContainsKey("choices"))
                        throw new InvalidOperationException("Unexpected AI response envelope.");
                    var choices = envelope["choices"] as object[];
                    var choice = choices != null && choices.Length > 0
                        ? choices[0] as Dictionary<string, object> : null;
                    if (choice == null || !choice.ContainsKey("finish_reason") || !string.Equals(choice["finish_reason"] as string, "stop", StringComparison.Ordinal))
                        throw new InvalidOperationException("AI response incomplete.");
                    var message = choice != null && choice.ContainsKey("message")
                        ? choice["message"] as Dictionary<string, object> : null;
                    var content = message != null && message.ContainsKey("content")
                        ? message["content"] as string : null;
                    if (message != null && message.ContainsKey("refusal") && message["refusal"] != null)
                        throw new InvalidOperationException("AI declined the request.");
                    if (string.IsNullOrWhiteSpace(content) || content.Length > 16000)
                        throw new InvalidOperationException("Empty or oversize AI draft.");
                    return json.Deserialize<JobEditForm>(content);
                }
            }
        }

        private HttpResponseMessage SendDraftRequest(string body, CancellationToken token)
        {
            // Only explicit transient gateway failures are retried, with one shared 90-second budget.
            // Never retry a quota/authentication error, an ambiguous network timeout, or invalid output.
            for (var attempt = 0; ; attempt++)
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    var response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).GetAwaiter().GetResult();
                    var status = (int)response.StatusCode;
                    if (provider != "Gemini" || attempt >= 2 || (status != 502 && status != 503 && status != 504))
                        return response;
                    response.Dispose();
                }
                Task.Delay(TimeSpan.FromSeconds(1 << attempt), token).GetAwaiter().GetResult();
            }
        }

        private static string UserMessage(JobIdeaForm idea) =>
            "Loại dự án: " + idea.ProjectType + "\n" +
            "Ngân sách: " + idea.Budget + "\n" +
            "Ngành: " + idea.Industry + "\nHạn dự kiến: " + (idea.Deadline.HasValue ? idea.Deadline.Value.ToString("yyyy-MM-dd") : "Chưa xác định") + "\n" +
            "Góc soạn lần này: " + idea.GenerationAngle + "\nÝ tưởng của Client: " + idea.Idea + "\nƯu tiên bổ sung: " + idea.AdditionalInstructions +
            "\nDữ liệu tham khảo được phép từ kho tri thức (có thể trống):\n" + idea.KnowledgeContext;

        private static object OutputSchema(bool compact = false, string projectType = null)
        {
            var properties = new Dictionary<string, object>();
            var names = new[] { "Title", "Description", "Objectives", "Scope", "UserStories", "RequiredSkills", "Deliverables", "Milestones" };
            var limits = new[] { 180, 600, 350, 600, 450, 500, 500, 400 };
            for (var index = 0; index < names.Length; index++)
                properties[names[index]] = compact ? (object)new { type = "string", minLength = 1, maxLength = limits[index] } : new { type = "string" };
            properties["Roles"] = new {
                type = "array", items = new { type = "object", additionalProperties = false,
                    properties = new { Title = new { type = "string" }, RequiredCount = new { type = "integer" } },
                    required = new[] { "Title", "RequiredCount" } }
            };
            if (compact) properties["Roles"] = new {
                type = "array", minItems = 1, maxItems = projectType == "SOLO" ? 1 : 6,
                items = new { type = "object", additionalProperties = false,
                    properties = new { Title = new { type = "string", minLength = 1, maxLength = 100 }, RequiredCount = new { type = "integer", minimum = 1, maximum = projectType == "SOLO" ? 1 : 50 } },
                    required = new[] { "Title", "RequiredCount" } }
            };
            return new { type = "object", additionalProperties = false, properties, required = properties.Keys.ToArray() };
        }

        private static bool Valid(JobEditForm draft, string projectType)
        {
            if (draft == null || draft.Roles == null || draft.Roles.Count < 1 ||
                draft.Roles.Count > 6) return false;
            var fields = new[] { draft.Title, draft.Description, draft.Objectives, draft.Scope,
                draft.UserStories, draft.RequiredSkills, draft.Deliverables, draft.Milestones };
            var limits = new[] { 180, 4000, 2000, 2000, 2000, 1000, 2000, 2000 };
            for (var i = 0; i < fields.Length; i++)
                if (string.IsNullOrWhiteSpace(fields[i]) || fields[i].Length > limits[i])
                    return false;
            // Converted Project.Description is limited to 4000 characters.
            if (fields.Skip(1).Sum(s => s.Length) > 3600) return false;
            if (projectType == "SOLO" &&
                (draft.Roles.Count != 1 || draft.Roles[0] == null ||
                 draft.Roles[0].RequiredCount != 1)) return false;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return draft.Roles.All(r => r != null && !string.IsNullOrWhiteSpace(r.Title) &&
                r.Title.Trim().Length <= 100 && r.RequiredCount >= 1 &&
                r.RequiredCount <= 50 && names.Add(r.Title.Trim()));
        }
    }
}
