using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;

namespace AiInterview.Web.Services;

public sealed class RequestBodyLimitsOptions
{
    public int DefaultBytes { get; set; } = 262144;
    public int UploadBytes { get; set; } = 6291456;
    public Dictionary<string, int> Endpoints { get; set; } = new()
    {
        ["Interviews.Stream"] = 8192, ["Interviews.Answer"] = 131072, ["Interviews.Draft"] = 131072,
        ["Interviews.Create"] = 65536, ["Dashboard.PracticeWeak"] = 65536, ["Profile.Save"] = 32768,
        ["Profile.CreateJd"] = 131072, ["Classes.Join"] = 8192, ["Interviews.Mark"] = 8192,
        ["AiSettings.Save"] = 16384
    };
    public int Limit(string? controller, string? action) => controller == "Profile" && action == "Upload" ? UploadBytes
        : Endpoints.GetValueOrDefault($"{controller}.{action}", DefaultBytes);
    public static bool IsValid(RequestBodyLimitsOptions o) => o.DefaultBytes is >= 8192 and <= 1048576
        && o.UploadBytes is >= 5242880 and <= 10485760 && o.Endpoints.Values.All(x => x is >= 1024 and <= 1048576);
}

public sealed class RequestBodyLimitMiddleware(RequestDelegate next, IOptions<RequestBodyLimitsOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) { await next(context); return; }
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var limit = options.Value.Limit(action?.ControllerName, action?.ActionName);
        if (context.Request.ContentLength > limit) { await Reject(context); return; }
        // Pre-read a bounded body before model binding or any database/provider side effect.
        // This also covers chunked requests, which have no Content-Length.
        context.Request.EnableBuffering(30 * 1024, (long)limit + 65536);
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            long total = 0;
            int read;
            while ((read = await context.Request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), context.RequestAborted)) > 0)
            {
                total += read;
                if (total > limit) { await Reject(context); return; }
            }
            context.Request.Body.Position = 0;
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
        await next(context);
    }
    private static Task Reject(HttpContext context) => SafeHttpError.WriteAsync(context, 413, "request_too_large",
        "Nội dung gửi lên quá lớn. Hãy rút ngắn nội dung hoặc chọn tệp nhỏ hơn.", ct: context.RequestAborted);
}
