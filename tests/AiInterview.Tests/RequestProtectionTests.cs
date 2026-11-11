using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AiInterview.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiInterview.Tests;

public sealed class RequestProtectionTests
{
    public static IEnumerable<object[]> Policies() => new[]
    {
        new[] { "Account", "Login", "auth" }, new[] { "Account", "Register", "auth" },
        new[] { "Account", "ResendConfirmation", "email" }, new[] { "Account", "ConfirmEmail", "confirm-email" },
        new[] { "AiSettings", "Save", "ai-settings" }, new[] { "AiSettings", "TestConnection", "ai-settings" },
        new[] { "Interviews", "Stream", "ai-stream" }, new[] { "Interviews", "Answer", "answer" },
        new[] { "Interviews", "Draft", "draft" }, new[] { "Interviews", "Create", "session-create" },
        new[] { "Profile", "Upload", "upload-cv" }, new[] { "Profile", "AnalyzeCv", "ai-analysis" },
        new[] { "Profile", "CompareGap", "ai-analysis" }, new[] { "Classes", "Join", "class-join" },
        new[] { "Admin", "EditUser", "admin-write" }, new[] { "Lecturer", "Comment", "admin-write" }
    }.Select(x => x.Cast<object>().ToArray());

    [Theory, MemberData(nameof(Policies))]
    public async Task EverySensitiveEndpointRejectsRequestNPlusOneAsJson(string controller, string action, string policy)
    {
        await using var app = await CreateApp(new Dictionary<string, string?> { [$"RateLimiting:Policies:{policy}:PermitLimit"] = "2" }, controller, action);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/operation", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/operation", null)).StatusCode);
        var response = await client.PostAsync("/operation", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.RetryAfter!.Delta > TimeSpan.Zero);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("rate_limited", body.RootElement.GetProperty("code").GetString());
        Assert.True(body.RootElement.GetProperty("retryAfterSeconds").GetInt32() > 0);
    }

    [Fact]
    public async Task GlobalCoversUnclassifiedEndpointsAndUsersAreIsolated()
    {
        await using var app = await CreateApp(new Dictionary<string, string?> { ["RateLimiting:Policies:global:PermitLimit"] = "1" }, "Home", "Index");
        var a = app.GetTestClient(); a.DefaultRequestHeaders.Add("X-Test-User", "A");
        var b = app.GetTestClient(); b.DefaultRequestHeaders.Add("X-Test-User", "B");
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsync("/operation", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await a.PostAsync("/operation", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.PostAsync("/operation", null)).StatusCode);
    }

    [Fact]
    public async Task LoginUsesIpEvenWithDifferentAccountsAndReturnsHtml()
    {
        await using var app = await CreateApp(new Dictionary<string, string?> { ["RateLimiting:Policies:auth:PermitLimit"] = "1" }, "Account", "Login");
        var a = app.GetTestClient(); a.DefaultRequestHeaders.Add("X-Test-User", "A");
        var b = app.GetTestClient(); b.DefaultRequestHeaders.Add("X-Test-User", "B");
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsync("/operation", null)).StatusCode);
        var rejected = await b.PostAsync("/operation", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("text/html", rejected.Content.Headers.ContentType!.MediaType);
        Assert.Contains("/Account/Login", await rejected.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StreamAlwaysGetsJsonBeforeAnySseBytes()
    {
        await using var app = await CreateApp(new Dictionary<string, string?> { ["RateLimiting:Policies:ai-stream:PermitLimit"] = "1" }, "Interviews", "Stream");
        var client = app.GetTestClient();
        await client.PostAsync("/operation", null);
        var response = await client.PostAsync("/operation", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain("event:", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void MappedIpv6AndIpv4ShareAnonymousPartition()
    {
        var a = new DefaultHttpContext(); var b = new DefaultHttpContext();
        a.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.1"); b.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        Assert.Equal(RequestProtection.Partition(a, "auth"), RequestProtection.Partition(b, "auth"));
        Assert.NotEqual(RequestProtection.Partition(a, "auth"), RequestProtection.Partition(a, "email"));
    }

    [Fact]
    public void StreamConcurrencyLeaseReleasesAndDoesNotBlockAnotherUser()
    {
        var services = new ServiceCollection().AddLogging().AddRequestProtection(new ConfigurationBuilder().Build()).BuildServiceProvider();
        using (services)
        {
            var limiter = services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter!;
            DefaultHttpContext Context(string user)
            {
                var c = new DefaultHttpContext(); c.Request.Method = "POST";
                c.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], "test"));
                c.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new ControllerActionDescriptor { ControllerName = "Interviews", ActionName = "Stream" }), "test"));
                return c;
            }
            using var first = limiter.AttemptAcquire(Context("A")); using var second = limiter.AttemptAcquire(Context("A"));
            using var rejected = limiter.AttemptAcquire(Context("A")); using var other = limiter.AttemptAcquire(Context("B"));
            Assert.True(first.IsAcquired); Assert.True(second.IsAcquired); Assert.False(rejected.IsAcquired); Assert.True(other.IsAcquired);
            first.Dispose(); using var retry = limiter.AttemptAcquire(Context("A")); Assert.True(retry.IsAcquired);
        }
    }

    [Theory]
    [InlineData("Interviews", "Answer", 131072)]
    [InlineData("Interviews", "Draft", 131072)]
    [InlineData("Interviews", "Stream", 8192)]
    [InlineData("Profile", "Upload", 6291456)]
    [InlineData("Profile", "Save", 32768)]
    public async Task OversizedKnownOrChunkedBodyIsRejectedBeforeAction(string controller, string action, int bytes)
    {
        await using var app = await CreateApp(new(), controller, action);
        var client = app.GetTestClient(); client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        foreach (var chunked in new[] { false, true })
        {
            using HttpContent content = chunked ? new StreamContent(new NonSeekableBody(bytes + 1)) : new ByteArrayContent(new byte[bytes + 1]);
            var response = await client.PostAsync("/operation", content);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Contains("request_too_large", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/operation", new { answer = "test" })).StatusCode);
    }

    [Fact]
    public async Task ProductionFailureDoesNotReturnSqlOrStack()
    {
        await using var app = await CreateApp(new(), "Interviews", "Answer", true);
        var response = await app.GetTestClient().PostAsync("/operation", null);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SELECT", body); Assert.DoesNotContain("private-key", body); Assert.DoesNotContain("InvalidOperationException", body);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData(0, 60)] [InlineData(20, 0)] [InlineData(20, 3601)]
    public async Task InvalidLimitsFailAtStartup(int permits, int window)
    {
        await Assert.ThrowsAsync<OptionsValidationException>(() => CreateApp(new() { ["RateLimiting:Policies:auth:PermitLimit"] = permits.ToString(), ["RateLimiting:Policies:auth:WindowSeconds"] = window.ToString() }, "Account", "Login"));
    }

    private static async Task<WebApplication> CreateApp(Dictionary<string, string?> config, string controller, string action, bool fail = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(config);
        builder.Services.AddRequestProtection(builder.Configuration);
        builder.Services.AddOptions<RequestBodyLimitsOptions>();
        var app = builder.Build();
        app.UseRouting(); app.UseMiddleware<SafeErrorMiddleware>();
        app.Use(async (c, next) =>
        {
            if (c.Request.Headers["X-Test-User"].FirstOrDefault() is { } id)
                c.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "test"));
            await next();
        });
        app.UseRateLimiter(); app.UseMiddleware<RequestBodyLimitMiddleware>();
        app.MapPost("/operation", () => fail ? throw new InvalidOperationException("SELECT private-key FROM SecretTable") : Results.Ok(new { completed = true }))
            .WithMetadata(new ControllerActionDescriptor { ControllerName = controller, ActionName = action });
        try { await app.StartAsync(); return app; }
        catch { await app.DisposeAsync(); throw; }
    }

    private sealed class NonSeekableBody(int length) : Stream
    {
        private int remaining = length;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { var n = Math.Min(remaining, count); Array.Clear(buffer, offset, n); remaining -= n; return n; }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
