// Current source + fake HTTP boundary; no database, real credentials, or external requests.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Web.Razor;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;

internal sealed class AiFixtureHandler : HttpMessageHandler
{
    internal string Body;
    internal HttpStatusCode Status = HttpStatusCode.OK;
    internal string RequestBody;
    internal int Requests;
    internal bool Timeout;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Requests++;
        RequestBody = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (Timeout) throw new TaskCanceledException("fixture-secret");
        if (request.RequestUri.AbsoluteUri != "https://api.openai.com/v1/chat/completions")
            throw new Exception("Unexpected provider endpoint");
        return Task.FromResult(new HttpResponseMessage(Status) {
            Content = new StringContent(Body ?? "", Encoding.UTF8, "application/json") });
    }
}

internal sealed class AiFixtureGenerator : IJobDraftGenerator
{
    public string GeneratorName { get; set; }
    internal JobEditForm Draft;
    internal int Calls;
    public JobEditForm Generate(JobIdeaForm form) { Calls++; return Draft; }
}

internal static class TestJobStudioAiSource
{
    private static int passed;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private static JobIdeaForm Brief(bool external = true) { return new JobIdeaForm {
        Idea = "Thiết kế bộ nhận diện cho tiệm bánh, bàn giao logo và hướng dẫn sử dụng.",
        Industry = "Thiết kế & sáng tạo", ProjectType = "SOLO", Budget = 12000000,
        UseExternalAi = external, AdditionalInstructions = "Ưu tiên dễ in trên bao bì",
        KnowledgeContext = "Bài học: cần kiểm tra logo khi in một màu.",
        KnowledgeReferences = "Memory #17 / Dự án #9 / v2" }; }
    private static JobEditForm Draft() { return new JobEditForm {
        Title = "Thiết kế nhận diện tiệm bánh",
        Description = "Thiết kế logo và bộ nhận diện dùng trên bao bì tiệm bánh.",
        Objectives = "Logo nhận diện rõ khi in một màu; cần khách hàng xác nhận kích thước bao bì.",
        Scope = "Trong: logo, quy chuẩn màu. Ngoài: thiết kế website.",
        UserStories = "Chủ tiệm cần tệp logo có thể chỉnh sửa để đặt in túi bánh.",
        RequiredSkills = "Thiết kế nhận diện: xây dựng logo, kiểm tra khả năng in một màu.",
        Deliverables = "Logo vector và hướng dẫn; nghiệm thu khi mở được tệp và in rõ mẫu một màu.",
        Milestones = "1. Chốt hướng thiết kế: duyệt phác thảo.\n2. Bàn giao: duyệt bộ tệp logo và mẫu in.",
        Roles = new List<JobRoleForm> { new JobRoleForm { Title = "Thiết kế nhận diện", RequiredCount = 1 } } }; }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine(name + ": PASS"); }
    private static void Reject(Action action, string contains) {
        try { action(); } catch (InvalidOperationException ex) {
            Check(ex.Message.Contains(contains), "Unexpected safe error: " + ex.Message);
            Check(!ex.Message.Contains("fixture-secret"), "Provider detail leaked"); return;
        }
        throw new Exception("Expected rejection: " + contains);
    }
    private static JobEditForm Suggest(JobIdeaForm brief, JobEditForm draft, string origin = "OpenAI-test-model") {
        return JobStudioService.GenerateSuggestion(brief, new AiFixtureGenerator { GeneratorName = origin, Draft = draft });
    }
    private static string Wrap(JobEditForm draft) { return Json.Serialize(new {
        choices = new[] { new { finish_reason = "stop", message = new { content = Json.Serialize(draft) } } } }); }
    private static AIJobDraftVersion Revision(JobEditForm form, string projectType) {
        try {
            return (AIJobDraftVersion)typeof(JobStudioService).GetMethod("NewVersion", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { form, 2, "ClientEdited", projectType });
        } catch (TargetInvocationException ex) { throw ex.InnerException; }
    }

    public static int Main(string[] args) {
        try {
            Run("Explicit AI selection rejects silent fallback before database access", () => {
                var service = new JobStudioService(null, new RuleBasedJobDraftGenerator());
                Reject(() => service.Generate(Brief(), 7), "AI");
            });
            Run("AI success preserves structured Vietnamese content", () => {
                var result = Suggest(Brief(), Draft());
                Check(result.Description.Contains("tiệm bánh"), "Brief-specific content lost");
                Check(result.Roles[0].Title == "Thiết kế nhận diện", "Hiring role lost");
            });
            Run("Edited JD and roles map to a new version with unused editor rows removed", () => {
                var edited = Draft(); edited.Title = " Tên khách hàng đã chỉnh sửa ";
                edited.Roles[0].Title = " Chuyên viên thương hiệu ";
                edited.Objectives = "Mục tiêu khách hàng đã chỉnh sửa";
                while (edited.Roles.Count < 6) edited.Roles.Add(new JobRoleForm());
                var version = Revision(edited, "SOLO");
                Check(version.VersionNumber == 2 && version.Generator == "ClientEdited", "Revision provenance lost");
                Check(version.Title == edited.Title.Trim() && version.Objectives == edited.Objectives, "Edits were not preserved");
                Check(version.Roles.Count == 1 && version.Roles.Single().Title == "Chuyên viên thương hiệu", "Editor roles did not map correctly");
            });
            Run("Edited SOLO staffing and oversized descriptions are rejected before persistence", () => {
                var edited = Draft(); edited.Roles[0].RequiredCount = 2;
                Reject(() => Revision(edited, "SOLO"), "SOLO");
                edited = Draft(); edited.Description = new string('x', 3500); edited.Scope = new string('y', 900);
                Reject(() => Revision(edited, "TEAM"), "4.000");
            });
            Run("Rejects null and missing sections", () => {
                Reject(() => Suggest(Brief(), null), "nội dung");
                var draft = Draft(); draft.Objectives = " "; Reject(() => Suggest(Brief(), draft), "nội dung");
            });
            Run("Rejects field overflow instead of silently truncating", () => {
                var draft = Draft(); draft.RequiredSkills = new string('x', 1001);
                Reject(() => Suggest(Brief(), draft), "nội dung");
            });
            Run("Rejects aggregate overflow before creating a project", () => {
                var draft = Draft(); draft.Description = new string('x', 3500); draft.Objectives = new string('y', 900);
                Reject(() => Suggest(Brief(), draft), "4.000");
            });
            Run("Rejects invalid role counts and duplicate roles", () => {
                var brief = Brief(); brief.ProjectType = "TEAM";
                var draft = Draft(); draft.Roles[0].RequiredCount = 51;
                Reject(() => Suggest(brief, draft), "vị trí");
                draft = Draft(); draft.Roles.Add(new JobRoleForm { Title = " THIẾT KẾ NHẬN DIỆN ", RequiredCount = 1 });
                Reject(() => Suggest(brief, draft), "trùng");
            });
            Run("SOLO requires exactly one role and person", () => {
                var draft = Draft(); draft.Roles[0].RequiredCount = 2;
                Reject(() => Suggest(Brief(), draft), "SOLO");
            });
            Run("Rejects null, empty, too many, and oversize roles", () => {
                var brief = Brief(); brief.ProjectType = "TEAM";
                var draft = Draft(); draft.Roles = null; Reject(() => Suggest(brief, draft), "vị trí");
                draft = Draft(); draft.Roles.Clear(); Reject(() => Suggest(brief, draft), "vị trí");
                draft = Draft(); draft.Roles[0] = null; Reject(() => Suggest(brief, draft), "vị trí");
                draft = Draft(); draft.Roles[0].Title = new string('x', 101); Reject(() => Suggest(brief, draft), "vị trí");
                draft = Draft(); draft.Roles[0].Title = " "; Reject(() => Suggest(brief, draft), "vị trí");
                draft = Draft(); for (int i = 0; i < 6; i++) draft.Roles.Add(new JobRoleForm { Title = "Vai trò " + i });
                Reject(() => Suggest(brief, draft), "vị trí");
            });
            Run("Does not call external provider without request consent", () => {
                var generator = new AiFixtureGenerator { GeneratorName = "OpenAI-test-model", Draft = Draft() };
                Reject(() => JobStudioService.GenerateSuggestion(Brief(false), generator), "đồng ý");
                Check(generator.Calls == 0, "Provider called without consent");
            });
            Run("Explicit internal template remains available with honest provenance", () => {
                var generator = new RuleBasedJobDraftGenerator();
                var draft = JobStudioService.GenerateSuggestion(Brief(false), generator);
                Check(generator.GeneratorName == "RuleBasedDemo-v2" && draft != null, "Internal choice unavailable");
            });
            Run("Disabled knowledge clears posted context and references without querying", () => {
                var brief = Brief(); brief.UseProjectKnowledge = false; brief.UseSharedKnowledge = false;
                JobPrompt.AttachKnowledge(null, 7, brief);
                Check(brief.KnowledgeContext == "" && brief.KnowledgeReferences == "", "Posted knowledge was trusted");
            });
            Run("Timeout cannot create a tracked fallback draft", () => {
                var handler = new AiFixtureHandler { Timeout = true };
                using (var client = new HttpClient(handler)) {
                    var generator = new OpenAiJobDraftGenerator("fixture-only-key", "test-model", new RuleBasedJobDraftGenerator(), client);
                    Reject(() => new JobStudioService(null, generator).Generate(Brief(), 7), "AI");
                }
            });
            foreach (var status in new[] { HttpStatusCode.Unauthorized, (HttpStatusCode)429, HttpStatusCode.BadGateway }) {
                var captured = status;
                Run("HTTP " + (int)captured + " cannot save a replacement template", () => {
                    var handler = new AiFixtureHandler { Status = captured, Body = "fixture-secret" };
                    using (var client = new HttpClient(handler)) {
                        var generator = new OpenAiJobDraftGenerator("fixture-only-key", "test-model", new RuleBasedJobDraftGenerator(), client);
                        Reject(() => new JobStudioService(null, generator).Generate(Brief(), 7), "AI");
                        Check(handler.Requests == 1, "Expected a single intercepted request");
                    }
                });
            }
            foreach (var body in new[] { "invalid-json", "{}", "{\"choices\":[{\"message\":{\"content\":null,\"refusal\":\"fixture-secret\"}}]}", new string('x', 60001) }) {
                var captured = body;
                Run("Invalid/refused provider reply cannot persist fallback", () => {
                    var handler = new AiFixtureHandler { Body = captured };
                    using (var client = new HttpClient(handler)) {
                        var generator = new OpenAiJobDraftGenerator("fixture-only-key", "test-model", new RuleBasedJobDraftGenerator(), client);
                        Reject(() => new JobStudioService(null, generator).Generate(Brief(), 7), "AI");
                    }
                });
            }
            Run("Provider request preserves consented brief, knowledge and store=false", () => {
                var handler = new AiFixtureHandler { Body = Wrap(Draft()) };
                using (var client = new HttpClient(handler)) {
                    var brief = Brief();
                    var generator = new OpenAiJobDraftGenerator("fixture-only-key", "test-model", new RuleBasedJobDraftGenerator(), client);
                    var draft = JobStudioService.GenerateSuggestion(brief, generator);
                    var request = Json.Deserialize<Dictionary<string, object>>(handler.RequestBody);
                    Check((bool)request["store"] == false, "store must be false");
                    var format = (Dictionary<string,object>)request["response_format"];
                    var schema = (Dictionary<string,object>)format["json_schema"];
                    Check((string)format["type"] == "json_schema" && (bool)schema["strict"], "Strict structured output schema required");
                    Check(handler.RequestBody.Contains(brief.Idea) && handler.RequestBody.Contains(brief.KnowledgeContext)
                        && handler.RequestBody.Contains(brief.AdditionalInstructions), "Authorized context missing");
                    Check(!handler.RequestBody.Contains("fixture-only-key"), "Secret in payload");
                    Check(draft.Title == Draft().Title, "AI output replaced");
                }
            });
            Run("Job Studio views parse as Razor and do not expose server prompt", () => {
                Check(args.Length == 1, "Expected Job Studio view directory");
                foreach (var name in new[] { "New", "Edit", "Index" }) {
                    var source = File.ReadAllText(Path.Combine(args[0], name + ".cshtml"));
                    var engine = new RazorTemplateEngine(new RazorEngineHost(new CSharpRazorCodeLanguage()));
                    var parsed = engine.GenerateCode(new StringReader(source));
                    Check(!parsed.ParserErrors.Any(), name + " has Razor syntax errors");
                    Check(!source.Contains("JobPrompt.System") && !source.Contains("JobPrompt.Preview") && !source.Contains("prompt-template"), "Server prompt exposed by view");
                }
            });
            Console.WriteLine("AI current-source tests: " + passed + " passed; zero external requests; no SQL or web build.");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
    }
}
