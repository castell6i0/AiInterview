using System.Net;
using System.Text.Json;
using AiInterview.Infrastructure.AI;
using AiInterview.Web.Controllers;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiInterview.Tests;

public sealed class AiSettingsWebTests
{
    [Theory]
    [InlineData("Gemini", "generativelanguage.googleapis.com", "/v1beta/models/gemini-test:streamGenerateContent")]
    [InlineData("OpenAI", "api.openai.com", "/v1/responses")]
    public async Task ProbeGeneratesValidatedSampleWithSavedKeyOnlyInHeader(string provider, string host, string path)
    {
        var settings = Configuration(provider);
        await settings.ResolveAsync(null);
        var handler = new CaptureHandler(HttpStatusCode.OK, SampleStream(provider));
        var result = await new AiConnectionCheckService(new ClientFactory(handler), settings).CheckAsync(default);
        Assert.True(result.Succeeded);
        Assert.Contains("đã tạo được nội dung mẫu đúng định dạng", result.Message);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(host, handler.Uri!.Host);
        Assert.Equal(path, handler.Uri.AbsolutePath);
        Assert.Equal("https", handler.Uri.Scheme);
        Assert.Equal(provider == "Gemini" ? "?alt=sse" : "", handler.Uri.Query);
        Assert.Equal("synthetic-credential", handler.Key);
        Assert.DoesNotContain("synthetic-credential", handler.Body);
        Assert.DoesNotContain("UserId", handler.Body);
        Assert.DoesNotContain("SessionId", handler.Body);
        using var body = JsonDocument.Parse(handler.Body!);
        if (provider == "OpenAI")
        {
            Assert.Equal("openai-test", body.RootElement.GetProperty("model").GetString());
            Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
            Assert.False(body.RootElement.GetProperty("store").GetBoolean());
            Assert.Equal(512, body.RootElement.GetProperty("max_output_tokens").GetInt32());
            var format = body.RootElement.GetProperty("text").GetProperty("format");
            Assert.Equal("json_schema", format.GetProperty("type").GetString());
            Assert.True(format.GetProperty("strict").GetBoolean());
        }
        else
        {
            var config = body.RootElement.GetProperty("generationConfig");
            Assert.Equal(512, config.GetProperty("maxOutputTokens").GetInt32());
            Assert.Equal("APPLICATION_JSON", config.GetProperty("responseFormat").GetProperty("text").GetProperty("mimeType").GetString());
        }
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain("synthetic-credential", result.Message);
    }

    [Theory]
    [InlineData("OpenAI", "{}")]
    [InlineData("OpenAI", "{\"ready\":false}")]
    [InlineData("OpenAI", "{\"ready\":\"true\"}")]
    [InlineData("OpenAI", "{\"ready\":true,\"extra\":1}")]
    [InlineData("Gemini", "{}")]
    [InlineData("Gemini", "{\"ready\":false}")]
    [InlineData("Gemini", "not json")]
    public async Task HttpSuccessWithInvalidGeneratedSampleDoesNotPass(string provider, string sample)
    {
        var settings = Configuration(provider);
        await settings.ResolveAsync(null);
        var handler = new CaptureHandler(HttpStatusCode.OK, SampleStream(provider, sample));
        var result = await new AiConnectionCheckService(new ClientFactory(handler), settings).CheckAsync(default);
        Assert.False(result.Succeeded);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("FINISH_REASON_UNSPECIFIED")]
    public async Task GeminiValidJsonWithoutTerminalCompletionDoesNotPass(string? finishReason)
    {
        var settings = Configuration("Gemini");
        await settings.ResolveAsync(null);
        var payload = "data: " + JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = "{\"ready\":true}" } } }, finishReason } }
        }) + "\n\ndata: [DONE]\n\n";
        var result = await new AiConnectionCheckService(new ClientFactory(new CaptureHandler(HttpStatusCode.OK, payload)), settings).CheckAsync(default);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GeminiStopAfterContentAndBeforeUsageCompletesSample()
    {
        var settings = Configuration("Gemini");
        await settings.ResolveAsync(null);
        var payload = "data: " + JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = "{\"ready\":true}" } } } } }
        }) + "\n\ndata: {\"candidates\":[{\"finishReason\":\"STOP\"}]}\n\n"
            + "data: {\"usageMetadata\":{\"promptTokenCount\":10,\"candidatesTokenCount\":5}}\n\n";
        var result = await new AiConnectionCheckService(new ClientFactory(new CaptureHandler(HttpStatusCode.OK, payload)), settings).CheckAsync(default);
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(400, "{\"reason\":\"API_KEY_INVALID\"}", "AI hệ thống chưa dùng được")]
    [InlineData(429, "{\"violations\":[{\"quotaId\":\"GenerateRequestsPerDay\"}]}", "hết quota")]
    public async Task GeminiHttpSuccessWithStreamErrorRetainsProviderRecoveryMessage(int status, string detail, string expected)
    {
        var settings = Configuration("Gemini");
        await settings.ResolveAsync(null);
        var payload = "data: {\"error\":{\"code\":" + status
            + ",\"message\":\"private-provider-response synthetic-credential\",\"details\":[" + detail + "]}}\n\n";
        var handler = new CaptureHandler(HttpStatusCode.OK, payload);
        var result = await new AiConnectionCheckService(new ClientFactory(handler), settings).CheckAsync(default);
        Assert.False(result.Succeeded);
        Assert.Contains(expected, result.Message);
        Assert.DoesNotContain("private-provider-response", result.Message);
        Assert.DoesNotContain("synthetic-credential", result.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task BillingFailureIncludingHttpSuccessStreamShowsActionableFailureWithoutRetry(HttpStatusCode status)
    {
        var settings = Configuration("OpenAI");
        await settings.ResolveAsync(null);
        var payload = "{\"error\":{\"code\":\"credit_balance_exhausted\",\"type\":\"insufficient_quota\",\"message\":\"private-provider-response synthetic-credential\"}}";
        if (status == HttpStatusCode.OK)
            payload = "data: {\"type\":\"response.failed\",\"response\":{\"status\":\"failed\",\"error\":{\"code\":\"credit_balance_exhausted\",\"message\":\"private-provider-response synthetic-credential\"}}}\n\n";
        var handler = new CaptureHandler(status, payload);
        var result = await new AiConnectionCheckService(new ClientFactory(handler), settings).CheckAsync(default);
        Assert.False(result.Succeeded);
        Assert.Contains("AI hệ thống đang quá tải hoặc hết quota", result.Message);
        Assert.DoesNotContain("tạm thời không khả dụng", result.Message);
        Assert.DoesNotContain("private-provider-response", result.Message);
        Assert.DoesNotContain("synthetic-credential", result.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("private-provider-response synthetic-credential")]
    [InlineData("{\"id\":\"openai-test\",\"object\":\"model\"}")]
    [InlineData("data: {\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"ready\\\":true}\"}\n\ndata: [DONE]\n\n")]
    public async Task HttpSuccessWithoutCompletedGenerationDoesNotPass(string payload)
    {
        var settings = Configuration("OpenAI");
        await settings.ResolveAsync(null);
        var result = await new AiConnectionCheckService(new ClientFactory(new CaptureHandler(HttpStatusCode.OK, payload)), settings).CheckAsync(default);
        Assert.False(result.Succeeded);
        Assert.DoesNotContain("private-provider-response", result.Message);
        Assert.DoesNotContain("synthetic-credential", result.Message);
    }

    [Fact]
    public async Task OversizedProviderResponseIsRejected()
    {
        var settings = Configuration("OpenAI");
        await settings.ResolveAsync(null);
        var handler = new CaptureHandler(HttpStatusCode.OK, new string('x', 64 * 1024 + 1));
        Assert.False((await new AiConnectionCheckService(new ClientFactory(handler), settings).CheckAsync(default)).Succeeded);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task ProviderErrorsNeverReturnRawResponseContent(HttpStatusCode status)
    {
        var settings = Configuration("OpenAI");
        await settings.ResolveAsync(null);
        var result = await new AiConnectionCheckService(new ClientFactory(new CaptureHandler(status)), settings).CheckAsync(default);
        Assert.False(result.Succeeded);
        Assert.DoesNotContain("private-provider-response", result.Message);
        Assert.DoesNotContain("synthetic-credential", result.Message);
    }

    [Fact]
    public async Task MissingAndInvalidConfigurationDoesNotSendAnyRequest()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK);
        var missing = Configuration("OpenAI", key: "");
        await missing.ResolveAsync(null);
        Assert.False((await new AiConnectionCheckService(new ClientFactory(handler), missing).CheckAsync(default)).Succeeded);
        var invalid = Configuration("OpenAI", model: "../private-path");
        await invalid.ResolveAsync(null);
        Assert.False((await new AiConnectionCheckService(new ClientFactory(handler), invalid).CheckAsync(default)).Succeeded);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task InvalidFormRedirectsWithoutEchoingAttemptedCredential()
    {
        var context = new DefaultHttpContext();
        var controller = new AiSettingsController(null!, null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, new EmptyTempDataProvider())
        };
        const string privateValue = "synthetic-entered-secret";
        controller.ModelState.SetModelValue("ApiKey", new ValueProviderResult(privateValue));
        controller.ModelState.AddModelError("ApiKey", "invalid");
        var input = new SaveAiSettingsInput { Provider = "OpenAI", Model = "", ApiKey = privateValue };
        var result = Assert.IsType<RedirectToActionResult>(await controller.Save(input, default));
        Assert.Equal("Index", result.ActionName);
        Assert.Null(input.ApiKey);
        Assert.Empty(controller.ModelState);
        Assert.DoesNotContain(privateValue, string.Join(" ", controller.TempData.Values));
        Assert.DoesNotContain(privateValue, string.Join(" ", result.RouteValues!.Values));
        Assert.DoesNotContain(typeof(AiSettingsViewModel).GetProperties(), p => p.Name == "ApiKey");
    }

    [Fact]
    public void MutationsRequireLoginAndAntiforgery()
    {
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(AiSettingsController), typeof(AuthorizeAttribute)));
        foreach (var name in new[] { "Save", "DeleteKey", "UseServerDefaults", "TestConnection" })
        {
            var method = typeof(AiSettingsController).GetMethod(name)!;
            Assert.NotNull(Attribute.GetCustomAttribute(method, typeof(HttpPostAttribute)));
            Assert.NotNull(Attribute.GetCustomAttribute(method, typeof(ValidateAntiForgeryTokenAttribute)));
        }
    }

    [Theory]
    [InlineData("/Profile/Cv/42", "/Profile/Cv/42")]
    [InlineData("/Dashboard#practice", "/Dashboard#practice")]
    [InlineData("https://example.invalid/collect", null)]
    [InlineData("//example.invalid/collect", null)]
    [InlineData("/\\example.invalid/collect", null)]
    [InlineData("javascript:alert(1)", null)]
    public async Task FailedSaveKeepsOnlyLocalReturnRoute(string returnUrl, string? expected)
    {
        var context = new DefaultHttpContext();
        var controller = new AiSettingsController(null!, null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, new EmptyTempDataProvider())
        };
        controller.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(new ActionContext(context,
            new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        controller.ModelState.AddModelError("ApiKey", "invalid");
        var result = Assert.IsType<RedirectToActionResult>(await controller.Save(new SaveAiSettingsInput
        {
            Provider = "Gemini", ApiKey = "synthetic-secret", ReturnUrl = returnUrl
        }, default));
        Assert.Equal("Index", result.ActionName);
        Assert.Equal(expected, result.RouteValues!["returnUrl"]);
        Assert.DoesNotContain("synthetic-secret", string.Join(" ", result.RouteValues.Values));
    }

    private static CurrentUserAiConfiguration Configuration(string provider, string key = "synthetic-credential", string? model = null) =>
        new(null!, Options.Create(new AiProviderOptions { Provider = provider }),
            Options.Create(new GeminiOptions { ApiKey = key, Model = model ?? "gemini-test" }),
            Options.Create(new OpenAiOptions { ApiKey = key, Model = model ?? "openai-test" }));

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static string SampleStream(string provider, string sample = "{\"ready\":true}") => "data: " + (provider == "OpenAI"
        ? JsonSerializer.Serialize(new
        {
            type = "response.completed",
            response = new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = sample } } } } }
        })
        : JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = sample } } }, finishReason = "STOP" } }
        })) + "\n\n";

    private sealed class CaptureHandler(HttpStatusCode status, string payload = "private-provider-response synthetic-credential") : HttpMessageHandler
    {
        public Uri? Uri;
        public HttpMethod? Method;
        public string? Key;
        public string? Body;
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Uri = request.RequestUri; Method = request.Method;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Key = request.Headers.Authorization?.Parameter ?? request.Headers.GetValues("x-goog-api-key").Single();
            return new HttpResponseMessage(status) { Content = new StringContent(payload) };
        }
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
