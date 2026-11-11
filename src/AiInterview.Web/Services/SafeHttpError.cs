using System.Data.Common;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;

namespace AiInterview.Web.Services;

public static class SafeHttpError
{
    public static bool WantsJson(HttpContext context)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        return context.Request.Headers.Accept.Any(x => x != null && (x.Contains("application/json", StringComparison.OrdinalIgnoreCase)
                || x.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase)))
            || context.Request.Headers["X-Requested-With"] == "XMLHttpRequest"
            || (action?.ControllerName == "Interviews" && action.ActionName is "Stream" or "Answer" or "Draft");
    }

    public static async Task WriteAsync(HttpContext context, int status, string code, string message,
        int? retryAfterSeconds = null, string? feature = null, CancellationToken ct = default)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        if (WantsJson(context))
        {
            await context.Response.WriteAsJsonAsync(new { code, message, retryAfterSeconds, feature }, ct);
            return;
        }
        context.Response.ContentType = "text/html; charset=utf-8";
        var controller = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerName;
        var back = controller switch { "AiSettings" => "/AiSettings", "Interviews" => "/Dashboard/History", "Profile" => "/Profile",
            "Classes" => "/Classes", "Lecturer" => "/Lecturer", "Admin" => "/Admin", "Account" => "/Account/Login", _ => "/" };
        await context.Response.WriteAsync($"<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Vui lòng thử lại · InterviewLab</title></head><body style=\"background:#f5f7f2;color:#202d29;font:18px/1.7 Arial,sans-serif\"><main style=\"max-width:620px;margin:12vh auto;padding:32px\"><h1>Chưa thể thực hiện thao tác</h1><p role=\"alert\">{HtmlEncoder.Default.Encode(message)}</p><p>Bạn có thể dùng nút Quay lại của trình duyệt để kiểm tra nội dung vừa nhập.</p><a style=\"display:inline-block;padding:10px 18px;border-radius:10px;background:#234e40;color:white;text-decoration:none\" href=\"{back}\">Quay lại →</a></main></body></html>", ct);
    }
}

public sealed class SafeErrorMiddleware(RequestDelegate next, ILogger<SafeErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception ex) when (ex is DbException or DbUpdateException or KeyNotFoundException or BadHttpRequestException
            || !context.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
        {
            // Exception text/stack may contain SQL, parameter values, server paths, or a provider body. Never log it.
            logger.LogWarning("Request failed: type {FailureType}; trace {TraceId}", ex.GetType().Name, context.TraceIdentifier);
            if (context.Response.HasStarted) { context.Abort(); return; }
            var status = ex is KeyNotFoundException ? 404 : ex is BadHttpRequestException bad ? bad.StatusCode : 500;
            var message = status switch { 413 => "Nội dung gửi lên quá lớn. Hãy rút ngắn nội dung hoặc chọn tệp nhỏ hơn.",
                404 => "Không tìm thấy nội dung hoặc bạn không có quyền truy cập.",
                400 => "Yêu cầu chưa hợp lệ. Vui lòng kiểm tra lại nội dung.",
                _ => "Chưa thể xử lý yêu cầu. Vui lòng thử lại sau; nội dung đã lưu vẫn được giữ." };
            context.Response.Clear();
            await SafeHttpError.WriteAsync(context, status, status == 413 ? "request_too_large" : "request_failed", message, ct: context.RequestAborted);
        }
    }
}
