// Run via TestJobStudioAiProvider.ps1. All HTTP replies are in-memory fixtures.
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;

internal sealed class FakeProviderHandler : HttpMessageHandler
{
    private readonly string responseBody;
    private readonly HttpStatusCode status;
    internal int Requests;
    internal string RequestBody;
    internal string AuthScheme;
    internal string RequestUri;

    internal FakeProviderHandler(HttpStatusCode status, string responseBody)
    {
        this.status = status;
        this.responseBody = responseBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Requests++;
        RequestUri = request.RequestUri.AbsoluteUri;
        AuthScheme = request.Headers.Authorization == null
            ? null : request.Headers.Authorization.Scheme;
        RequestBody = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var response = new HttpResponseMessage(status);
        response.Content = new StringContent(responseBody ?? "", Encoding.UTF8, "application/json");
        return Task.FromResult(response);
    }
}

internal static class TestJobStudioAiProvider
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    private static void Check(bool condition, string failure)
    {
        if (!condition) throw new Exception(failure);
    }

    private static string WrapDraft(JobEditForm draft)
    {
        return Json.Serialize(new
        {
            choices = new[] { new { finish_reason = "stop", message = new { content = Json.Serialize(draft) } } }
        });
    }

    private static void Exercise(string name, JobIdeaForm idea, string body,
        HttpStatusCode status, bool expectExternal)
    {
        var fallback = new RuleBasedJobDraftGenerator();
        var handler = new FakeProviderHandler(status, body);
        using (var client = new HttpClient(handler))
        {
            var generator = new OpenAiJobDraftGenerator("fixture-only-key", "test-model", fallback, client);
            var draft = generator.Generate(idea);
            Check(handler.Requests == 1, name + ": expected one intercepted request");
            Check(handler.RequestUri == "https://api.openai.com/v1/chat/completions",
                name + ": unexpected endpoint");
            Check(handler.AuthScheme == "Bearer", name + ": missing auth scheme");
            Check(handler.RequestBody.Contains("\"store\":false"), name + ": store must be disabled");
            Check(!handler.RequestBody.Contains("fixture-only-key"), name + ": credential in body");
            Check(handler.RequestBody.Contains(idea.Idea), name + ": idea absent from request");
            Check(draft != null, name + ": draft is null");
            if (expectExternal)
            {
                Check(generator.GeneratorName == "OpenAI-test-model", name + ": incorrect origin");
                Check(draft.Title == "Bản gợi ý đã kiểm tra", name + ": unexpected AI draft");
            }
            else
            {
                Check(generator.GeneratorName == "RuleBasedDemo-v2", name + ": wrong fallback origin");
                Check(draft.Title == fallback.Generate(idea).Title, name + ": fallback not returned");
            }
        }
        Console.WriteLine(name + ": PASS");
    }

    public static int Main()
    {
        try
        {
            var idea = new JobIdeaForm
            {
                Idea = "Xây dựng hệ thống quản lý các dự án freelancer",
                ProjectType = "SOLO", Budget = 12000000
            };
            var valid = new RuleBasedJobDraftGenerator().Generate(idea);
            valid.Title = "Bản gợi ý đã kiểm tra";
            Exercise("Fake success", idea, WrapDraft(valid), HttpStatusCode.OK, true);

            Exercise("Provider HTTP failure", idea, "{}", HttpStatusCode.BadGateway, false);
            Exercise("Invalid provider JSON", idea, "invalid-json", HttpStatusCode.OK, false);

            valid.Roles[0].RequiredCount = 2; // Invalid for SOLO projects.
            Exercise("Invalid SOLO roles", idea, WrapDraft(valid), HttpStatusCode.OK, false);

            Console.WriteLine("Job Studio fake provider: 4/4 PASS; zero external requests.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Job Studio fake provider FAILED: " + ex);
            return 1;
        }
    }
}
