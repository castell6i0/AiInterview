using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AiInterview.Web.Services;

public sealed class RequestLimitRule
{
    public bool Enabled { get; set; } = true;
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
    public int QueueLimit { get; set; }
    public RequestLimitRule() { }
    public RequestLimitRule(int permits, int seconds) { PermitLimit = permits; WindowSeconds = seconds; }
}

public sealed class RequestProtectionOptions
{
    public Dictionary<string, RequestLimitRule> Policies { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["auth"] = new(20, 60), ["email"] = new(3, 300), ["confirm-email"] = new(10, 60),
        ["ai-settings"] = new(10, 60), ["ai-stream"] = new(10, 60), ["upload-cv"] = new(5, 600),
        ["answer"] = new(30, 60), ["draft"] = new(120, 60), ["class-join"] = new(5, 60),
        ["admin-write"] = new(60, 60), ["global"] = new(300, 60),
        ["ai-analysis"] = new(10, 60), ["session-create"] = new(10, 60)
    };
    public bool StreamConcurrencyEnabled { get; set; } = true;
    public int StreamConcurrencyLimit { get; set; } = 2;
    public int StreamConcurrencyQueueLimit { get; set; }
    public int ConcurrencyRetryAfterSeconds { get; set; } = 5;
    public int EmailAccountCooldownSeconds { get; set; } = 60;
    public static bool IsValid(RequestProtectionOptions o) => o.StreamConcurrencyLimit is >= 1 and <= 20
        && o.StreamConcurrencyQueueLimit is >= 0 and <= 100 && o.ConcurrencyRetryAfterSeconds is >= 1 and <= 3600
        && o.EmailAccountCooldownSeconds is >= 1 and <= 3600
        && new RequestProtectionOptions().Policies.Keys.All(o.Policies.ContainsKey)
        && o.Policies.Values.All(x => x.PermitLimit is >= 1 and <= 10000
            && x.WindowSeconds is >= 1 and <= 3600 && x.QueueLimit is >= 0 and <= 100);
}

// Endpoint selection uses MVC metadata, not a user-controlled URL spelling (e.g. /?controller=...).
public static class RequestProtection
{
    public static string? EndpointPolicy(HttpContext context)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        return action == null ? null : PolicyFor(action.ControllerName, action.ActionName, context.Request.Method);
    }

    public static string? PolicyFor(string controller, string action, string method)
    {
        if (controller == "Account" && action == "ConfirmEmail") return "confirm-email";
        if (!HttpMethods.IsPost(method)) return null;
        return (controller, action) switch
        {
            ("Account", "Login" or "Register") => "auth",
            ("Account", "ResendConfirmation") => "email",
            ("AiSettings", _) => "ai-settings",
            ("Interviews", "Stream") => "ai-stream",
            ("Interviews", "Answer") => "answer",
            ("Interviews", "Draft") => "draft",
            ("Interviews", "Create") or ("Dashboard", "PracticeWeak") => "session-create",
            ("Profile", "Upload") => "upload-cv",
            ("Profile", "AnalyzeCv" or "CompareGap") => "ai-analysis",
            ("Classes", "Join") => "class-join",
            ("Admin" or "Lecturer", _) => "admin-write",
            _ => null
        };
    }

    public static string Partition(HttpContext context, string policy)
    {
        var user = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        if (policy is "auth" or "email" or "confirm-email") user = null;
        var address = context.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        return policy + ":" + (user != null ? "u:" + user : "ip:" + (address?.ToString() ?? "unknown"));
    }

    public static IServiceCollection AddRequestProtection(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RequestProtectionOptions>().Bind(configuration.GetSection("RateLimiting"))
            .Validate(RequestProtectionOptions.IsValid, "RateLimiting: số request phải 1..10000, cửa sổ 1..3600 giây, hàng đợi 0..100; kiểm tra giới hạn đồng thời.")
            .ValidateOnStart();
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RequestProtectionOptions>>((options, limits) =>
        {
            var settings = limits.Value;
            // Three independent partitions; concurrency lease stays held until the entire SSE request ends.
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    Window(context, "global", settings.Policies["global"])),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    EndpointPolicy(context) is { } policy ? Window(context, policy, settings.Policies[policy])
                        : RateLimitPartition.GetNoLimiter("unclassified")),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    EndpointPolicy(context) == "ai-stream" && settings.StreamConcurrencyEnabled
                        ? RateLimitPartition.GetConcurrencyLimiter(Partition(context, "ai-concurrency"), _ => new ConcurrencyLimiterOptions
                        { PermitLimit = settings.StreamConcurrencyLimit, QueueLimit = settings.StreamConcurrencyQueueLimit, QueueProcessingOrder = QueueProcessingOrder.OldestFirst })
                        : RateLimitPartition.GetNoLimiter("no-stream")));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)) : settings.ConcurrencyRetryAfterSeconds;
                var policy = EndpointPolicy(http) ?? "global";
                var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Partition(http, policy))))[..16];
                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RequestProtection")
                    .LogWarning("Request rejected: feature {Feature}; endpoint {Endpoint}; wait {WaitSeconds}s; partition {PartitionHash}",
                        policy, http.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>()?.DisplayName ?? "unmatched", seconds, fingerprint);
                http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                await SafeHttpError.WriteAsync(http, 429, "rate_limited",
                    $"Bạn thao tác hơi nhanh, vui lòng chờ {seconds} giây rồi thử lại. Nội dung đang nhập vẫn được giữ.", seconds, policy, cancellationToken);
            };
        });
        services.AddRateLimiter(_ => { });
        return services;
    }

    private static RateLimitPartition<string> Window(HttpContext context, string policy, RequestLimitRule rule) =>
        rule.Enabled ? RateLimitPartition.GetFixedWindowLimiter(Partition(context, policy), _ => new FixedWindowRateLimiterOptions
        { PermitLimit = rule.PermitLimit, Window = TimeSpan.FromSeconds(rule.WindowSeconds), QueueLimit = rule.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst, AutoReplenishment = true })
        : RateLimitPartition.GetNoLimiter(policy + ":disabled");
}
