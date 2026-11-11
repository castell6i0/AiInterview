using System.Net;
using System.Text;
using System.Text.Json;
using AiInterview.Application.Interfaces;

namespace AiInterview.Infrastructure.AI;

internal sealed class GeminiUsage
{
    public int? Input { get; private set; }
    public int? Output { get; private set; }
    public int? Thinking { get; private set; }
    public int? Total { get; private set; }

    public void Read(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
        Input = Count(usage, "promptTokenCount") ?? Input;
        Output = Count(usage, "candidatesTokenCount") ?? Output;
        Thinking = Count(usage, "thoughtsTokenCount") ?? Thinking;
        Total = Count(usage, "totalTokenCount") ?? Total;
    }

    private static int? Count(JsonElement source, string key) => source.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) && count >= 0 ? count : null;

    public decimal? Cost(GeminiOptions options)
    {
        if (Input is null || Output is null) return null;
        if (options.IsFreeTier) return 0m;
        if (options.InputPricePerMillionTokensUsd is not >= 0 || options.OutputPricePerMillionTokensUsd is not >= 0) return null;
        // An omitted thinking count is unknown unless the aggregate count proves there are no extra tokens.
        var thinking = Thinking ?? (Total == (long)Input + Output ? 0 : (int?)null);
        if (thinking is null) return null;
        return decimal.Round((Input.Value * options.InputPricePerMillionTokensUsd.Value
            + ((decimal)Output.Value + thinking.Value) * options.OutputPricePerMillionTokensUsd.Value) / 1_000_000m, 10);
    }
}

internal static class GeminiProtocol
{
    internal const int MaxResponseCharacters = 1_000_000;
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 64,
        PropertyNameCaseInsensitive = true
    };

    public static string ReadChunk(string payload, GeminiUsage usage)
    {
        using var doc = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 64 });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw Malformed();
        usage.Read(root);
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n) ? n : 500;
            throw Error((HttpStatusCode)code, payload);
        }
        if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.ValueKind == JsonValueKind.Object
            && feedback.TryGetProperty("blockReason", out var block)
            && block.ValueKind == JsonValueKind.String && block.GetString() is { Length: > 0 } blockReason && blockReason != "BLOCK_REASON_UNSPECIFIED")
            throw Blocked();

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            return ""; // Usage-only final SSE events are legal; final accumulated JSON is validated separately.
        var candidate = candidates[0];
        if (candidate.ValueKind != JsonValueKind.Object) throw Malformed();
        if (candidate.TryGetProperty("finishReason", out var finish))
        {
            var reason = finish.ValueKind == JsonValueKind.String ? finish.GetString() : null;
            if (reason is "SAFETY" or "RECITATION" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII" or "MODEL_ARMOR") throw Blocked();
            if (reason is "MAX_TOKENS") throw new AiServiceException("truncated", "Phản hồi AI vượt giới hạn độ dài. Hệ thống đã giữ dữ liệu để bạn có thể thử lại.");
            if (reason is not (null or "STOP" or "FINISH_REASON_UNSPECIFIED")) throw Malformed();
        }
        var text = new StringBuilder();
        if (candidate.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object
            && content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object) throw Malformed();
                // Thought summaries are not the requested output and must never be rendered as interview content.
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
                if (part.TryGetProperty("text", out var partText) && partText.ValueKind == JsonValueKind.String)
                    text.Append(partText.GetString());
            }
        if (text.Length > MaxResponseCharacters) throw Malformed();
        return text.ToString();
    }

    public static T Parse<T>(string text, Func<T, string?> validate)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxResponseCharacters) throw Malformed();
        try
        {
            var value = JsonSerializer.Deserialize<T>(text, JsonOptions);
            if (value is null || validate(value) is not null) throw Malformed();
            return value;
        }
        catch (JsonException) { throw Malformed(); }
    }

    public static AiServiceException Error(HttpStatusCode statusCode, string response)
    {
        var invalidKey = false;
        var dailyQuota = false;
        var schemaRejected = false;
        var requestTooLarge = false;
        try
        {
            using var doc = JsonDocument.Parse(response);
            // Classify known provider failures without retaining or displaying its message. A provider
            // error can echo private input, so only these fixed status codes cross the boundary.
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var errorInfo)
                && errorInfo.ValueKind == JsonValueKind.Object && errorInfo.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                var text = message.GetString()!;
                schemaRejected = (text.Contains("schema", StringComparison.OrdinalIgnoreCase)
                        && (text.Contains("complex", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("too large", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("too many", StringComparison.OrdinalIgnoreCase)))
                    || text.Contains("too many states", StringComparison.OrdinalIgnoreCase);
                requestTooLarge = text.Contains("input token count exceeds", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("request payload size exceeds", StringComparison.OrdinalIgnoreCase);
            }
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                invalidKey = details.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("reason", out var reason)
                    && reason.ValueKind == JsonValueKind.String
                    && reason.GetString() is "API_KEY_INVALID" or "API_KEY_EXPIRED" or "API_KEY_SERVICE_BLOCKED");
                dailyQuota = details.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.Object
                    && x.TryGetProperty("violations", out var violations) && violations.ValueKind == JsonValueKind.Array
                    && violations.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.Object
                        && v.TryGetProperty("quotaId", out var id) && id.ValueKind == JsonValueKind.String
                        && id.GetString()!.Contains("PerDay", StringComparison.OrdinalIgnoreCase)));
            }
        }
        catch (JsonException) { }
        if (invalidKey || statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new("authentication", "Key Gemini chưa hợp lệ hoặc chưa có quyền sử dụng. Mở Cài đặt AI để thay key và kiểm tra lại kết nối.");
        if (statusCode == HttpStatusCode.TooManyRequests && dailyQuota)
            return new("quota_daily", "Kết nối Gemini đã hết hạn mức trong ngày. Buổi luyện và điểm đã chấm được giữ. Bạn có thể chờ hạn mức được cấp lại hoặc mở Cài đặt AI để chọn kết nối khác.");
        if (statusCode == HttpStatusCode.BadRequest && schemaRejected)
            return new("schema_rejected", "Gemini chưa xử lý được yêu cầu của ứng dụng. Câu trả lời và điểm đã chấm vẫn được giữ. Vui lòng liên hệ quản trị viên để được hỗ trợ.");
        if (statusCode == HttpStatusCode.RequestEntityTooLarge || statusCode == HttpStatusCode.BadRequest && requestTooLarge)
            return new("request_too_large", "Nội dung buổi luyện vượt giới hạn của kết nối AI hiện tại. Dữ liệu vẫn được giữ. Mở Cài đặt AI để chọn model có giới hạn lớn hơn hoặc dùng kết nối khác.");
        return statusCode switch
        {
            HttpStatusCode.TooManyRequests => new("quota", "Gemini đang giới hạn lượt gọi hoặc đã hết hạn mức. Chờ một chút rồi thử lại, hoặc mở Cài đặt AI để chọn kết nối khác."),
            HttpStatusCode.NotFound => new("model_unavailable", "Tài khoản Gemini chưa dùng được model đang chọn. Mở Cài đặt AI để đổi model hoặc chọn kết nối khác."),
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => new("timeout", "Gemini phản hồi quá lâu. Dữ liệu của bạn đã được giữ để thử lại."),
            >= HttpStatusCode.InternalServerError => new("unavailable", "Gemini tạm thời không khả dụng. Vui lòng thử lại sau."),
            _ => new("invalid_request", "Gemini chưa chấp nhận yêu cầu. Mở Cài đặt AI để kiểm tra kết nối; nếu vẫn lỗi, hãy liên hệ quản trị viên.")
        };
    }

    public static bool CanRetry(AiServiceException error) => error.Code is "quota" or "timeout" or "unavailable" or "network" or "invalid_response" or "truncated";
    public static AiServiceException Malformed() => new("invalid_response", "AI trả về nội dung chưa đúng cấu trúc hoặc chưa đủ tiêu chí. Dữ liệu của bạn vẫn được giữ; vui lòng thử lại.");
    private static AiServiceException Blocked() => new("blocked", "Gemini không thể xử lý nội dung này theo bộ lọc của nhà cung cấp. Hãy kiểm tra nội dung rồi thử lại.");
}
