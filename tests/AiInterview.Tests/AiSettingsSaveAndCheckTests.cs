using System.Net;
using System.Security.Claims;
using System.Text.Json;
using AiInterview.Infrastructure.AI;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.Controllers;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiInterview.Tests;

public sealed class AiSettingsSaveAndCheckTests
{
    [SqlFact]
    public async Task SaveAndCheckUsesNewProviderModelAndKeyWithoutMutatingRequestSnapshot()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var user = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var options = new TestOptions();
        var settings = options.Settings(db);

        foreach (var selectedProvider in new[] { "Gemini", "OpenAI" })
        {
            var oldProvider = selectedProvider == "OpenAI" ? "Gemini" : "OpenAI";
            await settings.SaveAsync(user.Id, oldProvider, "synthetic-old-key", "old-model");
            var oldSnapshot = options.Snapshot(settings);
            await oldSnapshot.ResolveAsync(user.Id);
            var handler = new CaptureHandler(HttpStatusCode.OK, SuccessStream(selectedProvider));
            var controller = Controller(user.Id, settings, oldSnapshot, handler);
            var input = new SaveAiSettingsInput
            {
                Provider = selectedProvider, Model = "new-model", ApiKey = "synthetic-new-key",
                CheckConnection = true, ReturnUrl = "/Interviews/Create"
            };

            var redirect = Assert.IsType<RedirectToActionResult>(await controller.Save(input, default));

            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal(selectedProvider, redirect.RouteValues!["provider"]);
            Assert.Equal(input.ReturnUrl, redirect.RouteValues["returnUrl"]);
            Assert.Equal(1, handler.Calls);
            Assert.Equal("synthetic-new-key", handler.Key);
            Assert.Equal("new-model", handler.Model);
            Assert.Equal(selectedProvider == "OpenAI" ? "api.openai.com" : "generativelanguage.googleapis.com", handler.Host);
            Assert.Contains("AI đã tạo được", Assert.IsType<string>(controller.TempData["AiSettingsStatus"]));
            Assert.False(controller.TempData.ContainsKey("AiSettingsError"));
            Assert.Null(input.ApiKey);
            Assert.Empty(controller.ModelState);
            Assert.DoesNotContain("synthetic-new-key", string.Join(" ", controller.TempData.Values));
            Assert.Equal(oldProvider, oldSnapshot.Provider);
            Assert.Equal("old-model", oldSnapshot.Model);
            using var oldRequest = oldSnapshot.CreateConnectionProbeRequest();
            Assert.Equal("synthetic-old-key", ReadKey(oldRequest));
            var saved = await settings.GetAsync(user.Id);
            Assert.Equal(selectedProvider, saved.Provider);
            Assert.Equal("new-model", selectedProvider == "OpenAI" ? saved.OpenAiModel : saved.GeminiModel);
        }
    }

    [SqlFact]
    public async Task FailedCheckKeepsSavedSelectionAndExistingKeyWithoutFalseSuccessOrCredentialEcho()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var user = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var options = new TestOptions();
        var settings = options.Settings(db);
        await settings.SaveAsync(user.Id, "OpenAI", "synthetic-saved-key", "old-model");
        var snapshot = options.Snapshot(settings);
        await snapshot.ResolveAsync(user.Id);
        var handler = new CaptureHandler(HttpStatusCode.TooManyRequests,
            "{\"error\":{\"code\":\"credit_balance_exhausted\",\"type\":\"insufficient_quota\",\"message\":\"private-provider-response synthetic-saved-key\"}}");
        var controller = Controller(user.Id, settings, snapshot, handler);
        controller.TempData["AiSettingsStatus"] = "A stale success must not survive this failed check.";
        var input = new SaveAiSettingsInput { Provider = "OpenAI", Model = "new-model", CheckConnection = true };

        await controller.Save(input, default);

        var saved = await settings.GetAsync(user.Id);
        Assert.Equal("OpenAI", saved.Provider);
        Assert.Equal("new-model", saved.OpenAiModel);
        Assert.True(saved.HasOpenAiKey);
        Assert.Equal("synthetic-saved-key", handler.Key);
        Assert.Equal("new-model", handler.Model);
        Assert.Equal(1, handler.Calls);
        Assert.False(controller.TempData.ContainsKey("AiSettingsStatus"));
        var message = Assert.IsType<string>(controller.TempData["AiSettingsError"]);
        Assert.StartsWith("Đã lưu cấu hình AI riêng.", message);
        Assert.Contains("số dư", message);
        Assert.DoesNotContain("private-provider-response", message);
        Assert.DoesNotContain("synthetic-saved-key", message);
        Assert.Null(input.ApiKey);
    }

    [SqlFact]
    public async Task SaveWithoutCheckDoesNotSendProviderRequest()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var user = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var options = new TestOptions();
        var settings = options.Settings(db);
        var snapshot = options.Snapshot(settings);
        await snapshot.ResolveAsync(user.Id);
        var handler = new CaptureHandler(HttpStatusCode.OK, SuccessStream("Gemini"));
        var controller = Controller(user.Id, settings, snapshot, handler);

        await controller.Save(new SaveAiSettingsInput
        {
            Provider = "Gemini", Model = "saved-model", ApiKey = "synthetic-entered-key"
        }, default);

        Assert.Equal(0, handler.Calls);
        Assert.Equal("saved-model", (await settings.GetAsync(user.Id)).GeminiModel);
        Assert.Contains("Bấm Kiểm tra kết nối", Assert.IsType<string>(controller.TempData["AiSettingsStatus"]));
    }

    [SqlFact]
    public async Task CancellationDuringCheckPropagatesAfterSaveAndStillClearsEnteredCredential()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var user = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var options = new TestOptions();
        var settings = options.Settings(db);
        var snapshot = options.Snapshot(settings);
        await snapshot.ResolveAsync(user.Id);
        using var cancellation = new CancellationTokenSource();
        var handler = new CaptureHandler(HttpStatusCode.OK, SuccessStream("OpenAI"), cancellation);
        var controller = Controller(user.Id, settings, snapshot, handler);
        var input = new SaveAiSettingsInput
        {
            Provider = "OpenAI", Model = "saved-model", ApiKey = "synthetic-entered-key", CheckConnection = true
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.Save(input, cancellation.Token));

        Assert.Equal("saved-model", (await settings.GetAsync(user.Id)).OpenAiModel);
        Assert.Equal(1, handler.Calls);
        Assert.Null(input.ApiKey);
        Assert.Empty(controller.ModelState);
        Assert.False(controller.TempData.ContainsKey("AiSettingsStatus"));
        Assert.False(controller.TempData.ContainsKey("AiSettingsError"));
    }

    private static AiSettingsController Controller(string userId, UserAiSettingsService settings,
        CurrentUserAiConfiguration snapshot, CaptureHandler handler)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"))
        };
        var controller = new AiSettingsController(settings, snapshot,
            new AiConnectionCheckService(new ClientFactory(handler), snapshot), null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = context, RouteData = new RouteData() },
            TempData = new TempDataDictionary(context, new EmptyTempDataProvider())
        };
        controller.Url = new UrlHelper(controller.ControllerContext);
        return controller;
    }

    private sealed class TestOptions
    {
        private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
        private readonly IOptions<AiProviderOptions> provider = Options.Create(new AiProviderOptions { Provider = "Gemini" });
        private readonly IOptions<GeminiOptions> gemini = Options.Create(new GeminiOptions { ApiKey = "synthetic-server-gemini", Model = "server-gemini" });
        private readonly IOptions<OpenAiOptions> openAi = Options.Create(new OpenAiOptions { ApiKey = "synthetic-server-openai", Model = "server-openai" });
        public UserAiSettingsService Settings(AppDbContext db) => new(db, protection, provider, gemini, openAi, TimeProvider.System);
        public CurrentUserAiConfiguration Snapshot(UserAiSettingsService settings) => new(settings, provider, gemini, openAi);
    }

    private static string ReadKey(HttpRequestMessage request) =>
        request.Headers.Authorization?.Parameter ?? request.Headers.GetValues("x-goog-api-key").Single();

    private static string SuccessStream(string provider) => "data: " + (provider == "OpenAI"
        ? JsonSerializer.Serialize(new
        {
            type = "response.completed",
            response = new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = "{\"ready\":true}" } } } } }
        })
        : JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = "{\"ready\":true}" } } }, finishReason = "STOP" } }
        })) + "\n\n";

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CaptureHandler(HttpStatusCode status, string payload, CancellationTokenSource? cancel = null) : HttpMessageHandler
    {
        public int Calls;
        public string? Key;
        public string? Model;
        public string? Host;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Key = ReadKey(request);
            Host = request.RequestUri!.Host;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Model = Host == "api.openai.com" ? body.RootElement.GetProperty("model").GetString()
                : request.RequestUri.AbsolutePath.Split('/').Last().Split(':')[0];
            cancel?.Cancel();
            ct.ThrowIfCancellationRequested();
            return new HttpResponseMessage(status) { Content = new StringContent(payload) };
        }
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
