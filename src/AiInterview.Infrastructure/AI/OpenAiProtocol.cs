using System.Net;
using System.Text;
using System.Text.Json;
using AiInterview.Application.Interfaces;

namespace AiInterview.Infrastructure.AI;

internal sealed class OpenAiUsage
{
    public int? Input { get; private set; }
    public int? Output { get; private set; }
    public int? Thinking { get; private set; }
    public void Read(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
        Input = Count(usage, "input_tokens") ?? Input;
        Output = Count(usage, "output_tokens") ?? Output;
        if (usage.TryGetProperty("output_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object)
            Thinking = Count(details, "reasoning_tokens") ?? Thinking;
    }
    private static int? Count(JsonElement source, string key) => source.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) && count >= 0 ? count : null;
    public decimal? Cost(OpenAiOptions options) => Input is null || Output is null
        || options.InputPricePerMillionTokensUsd is not >= 0 || options.OutputPricePerMillionTokensUsd is not >= 0 ? null
        // Responses output_tokens already includes reasoning tokens. Never charge them twice.
        : decimal.Round((Input.Value * options.InputPricePerMillionTokensUsd.Value
            + Output.Value * options.OutputPricePerMillionTokensUsd.Value) / 1_000_000m, 10);
}

internal static class OpenAiProtocol
{
    public static string ReadResponse(string payload, OpenAiUsage usage)
    {
        using var document = JsonDocument.Parse(payload);
        return ReadResponse(document.RootElement, usage);
    }
    private static string ReadResponse(JsonElement root, OpenAiUsage usage)
    {
        if (root.ValueKind != JsonValueKind.Object) throw GeminiProtocol.Malformed();
        usage.Read(root);
        if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
        {
            if (error.ValueKind != JsonValueKind.Object) throw GeminiProtocol.Malformed();
            throw Error(null, root.GetRawText());
        }
        if (!root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
            throw GeminiProtocol.Malformed();
        if (status.GetString() != "completed")
        {
            var reason = root.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object
                && details.TryGetProperty("reason", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (reason == "max_output_tokens") throw new AiServiceException("truncated", "Phản hồi AI vượt giới hạn độ dài. Dữ liệu được giữ để bạn có thể thử lại.");
            if (reason == "content_filter") throw Blocked();
            throw GeminiProtocol.Malformed();
        }
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) throw GeminiProtocol.Malformed();
        var text = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw GeminiProtocol.Malformed();
            if (!item.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                throw GeminiProtocol.Malformed();
            if (type.GetString() != "message") continue;
            if (!item.TryGetProperty("content", out var parts) || parts.ValueKind != JsonValueKind.Array) throw GeminiProtocol.Malformed();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("type", out var partType)
                    || partType.ValueKind != JsonValueKind.String) throw GeminiProtocol.Malformed();
                if (partType.GetString() == "refusal") throw Blocked();
                if (partType.GetString() == "output_text")
                {
                    if (!part.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String)
                        throw GeminiProtocol.Malformed();
                    text.Append(value.GetString());
                }
            }
        }
        if (text.Length == 0 || text.Length > GeminiProtocol.MaxResponseCharacters) throw GeminiProtocol.Malformed();
        return text.ToString();
    }

    internal static async Task<string> ReadStreamAsync(Stream stream, OpenAiUsage usage, Func<string, Task> callback, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var accumulated = new StringBuilder();
        var data = new StringBuilder();
        string? completedText = null;
        async Task FlushAsync()
        {
            if (data.Length == 0) return;
            var payload = data.ToString();
            data.Clear();
            if (payload.Trim() == "[DONE]") return;
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                throw GeminiProtocol.Malformed();
            switch (type.GetString())
            {
                case "response.output_text.delta":
                    if (!root.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.String) throw GeminiProtocol.Malformed();
                    var chunk = delta.GetString()!;
                    if (accumulated.Length + chunk.Length > GeminiProtocol.MaxResponseCharacters) throw GeminiProtocol.Malformed();
                    accumulated.Append(chunk);
                    if (chunk.Length > 0) await callback(chunk);
                    break;
                case "response.refusal.delta":
                case "response.refusal.done": throw Blocked();
                case "response.failed":
                case "response.incomplete":
                case "response.completed":
                    if (!root.TryGetProperty("response", out var response)) throw GeminiProtocol.Malformed();
                    completedText = ReadResponse(response, usage);
                    break;
                case "error": throw Error(null, payload);
            }
        }
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0) { await FlushAsync(); continue; }
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var value = line[5..].TrimStart(' ');
            if (data.Length + value.Length > GeminiProtocol.MaxResponseCharacters) throw GeminiProtocol.Malformed();
            if (data.Length > 0) data.Append('\n');
            data.Append(value);
        }
        await FlushAsync();
        // A socket close or [DONE] marker alone is not proof that a response completed.
        if (completedText is null || accumulated.Length > 0 && accumulated.ToString() != completedText) throw GeminiProtocol.Malformed();
        return completedText;
    }

    public static AiServiceException Error(HttpStatusCode? statusCode, string payload)
    {
        string? code = null;
        string? type = null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var error = document.RootElement;
            if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("error", out var nested) && nested.ValueKind == JsonValueKind.Object) error = nested;
            if (error.ValueKind == JsonValueKind.Object)
            {
                code = StringProperty(error, "code");
                type = StringProperty(error, "type");
            }
        }
        catch (JsonException) { }
        // Never propagate the provider body: error strings can contain keys or private prompt fragments.
        // Responses can report a failure inside an HTTP 200 stream. Its code/type is more
        // specific than the transport status; a billing failure must never be retried as an outage.
        if (code is "insufficient_quota" or "billing_hard_limit_reached" or "credit_balance_exhausted"
            or "organization_spend_limit_exceeded" or "project_spend_limit_exceeded"
            or "organization_usage_limit_exceeded" or "usage_limit_exceeded" || type == "insufficient_quota")
            return new("quota_exhausted", "Tài khoản OpenAI chưa có hoặc đã hết hạn mức API. Kiểm tra số dư và giới hạn sử dụng trên OpenAI Platform; gói ChatGPT không bao gồm hạn mức API. Dữ liệu vẫn được giữ. Bạn cũng có thể mở Cài đặt AI để chọn kết nối khác.");
        if (code is "invalid_api_key" or "permission_denied" or "insufficient_permissions"
            || type is "authentication_error" or "permission_error")
            return new("authentication", "Key OpenAI chưa hợp lệ hoặc chưa có quyền sử dụng. Mở Cài đặt AI để thay key và kiểm tra lại kết nối.");
        if (code == "model_not_found")
            return new("model_unavailable", "Tài khoản OpenAI chưa dùng được model đang chọn. Mở Cài đặt AI để đổi model hoặc chọn kết nối khác.");
        if (code is "context_length_exceeded" or "request_too_large")
            return new("request_too_large", "Nội dung buổi luyện vượt giới hạn của kết nối OpenAI hiện tại. Dữ liệu vẫn được giữ. Mở Cài đặt AI để chọn model có giới hạn lớn hơn hoặc dùng kết nối khác.");
        if (code is "rate_limit_exceeded" or "slow_down" || type == "rate_limit_error")
            return new("quota", "OpenAI đang giới hạn lượt gọi. Chờ một chút rồi thử lại, hoặc mở Cài đặt AI để chọn kết nối khác.");
        if (code is "timeout" or "request_timeout" || type == "timeout_error")
            return new("timeout", "OpenAI phản hồi quá lâu. Dữ liệu đã được giữ để thử lại.");
        if (code is "server_error" or "server_is_overloaded" || type is "server_error" or "service_unavailable_error")
            return new("unavailable", "OpenAI tạm thời không khả dụng. Vui lòng thử lại sau.");
        if (code is "invalid_json_schema" or "unsupported_parameter" or "unsupported_value")
            return new("invalid_request", "OpenAI chưa chấp nhận yêu cầu. Mở Cài đặt AI để kiểm tra kết nối; nếu vẫn lỗi, hãy liên hệ quản trị viên.");
        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new("authentication", "Key OpenAI chưa hợp lệ hoặc chưa có quyền sử dụng. Mở Cài đặt AI để thay key và kiểm tra lại kết nối."),
            HttpStatusCode.RequestEntityTooLarge => new("request_too_large", "Nội dung buổi luyện vượt giới hạn của kết nối OpenAI hiện tại. Dữ liệu vẫn được giữ. Mở Cài đặt AI để chọn model có giới hạn lớn hơn hoặc dùng kết nối khác."),
            HttpStatusCode.NotFound => new("model_unavailable", "Tài khoản OpenAI chưa dùng được model đang chọn. Mở Cài đặt AI để đổi model hoặc chọn kết nối khác."),
            HttpStatusCode.TooManyRequests => new("quota", "OpenAI đang giới hạn lượt gọi. Chờ một chút rồi thử lại, hoặc mở Cài đặt AI để chọn kết nối khác."),
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => new("timeout", "OpenAI phản hồi quá lâu. Dữ liệu đã được giữ để thử lại."),
            >= HttpStatusCode.InternalServerError => new("unavailable", "OpenAI tạm thời không khả dụng. Vui lòng thử lại sau."),
            null when type == "invalid_request_error" => new("invalid_request", "OpenAI chưa chấp nhận yêu cầu. Mở Cài đặt AI để kiểm tra kết nối; nếu vẫn lỗi, hãy liên hệ quản trị viên."),
            // An unrecognized stream failure has no independent HTTP failure status.
            null => new("unavailable", "OpenAI tạm thời không khả dụng. Vui lòng thử lại sau."),
            _ => new("invalid_request", "OpenAI chưa chấp nhận yêu cầu. Mở Cài đặt AI để kiểm tra kết nối; nếu vẫn lỗi, hãy liên hệ quản trị viên.")
        };
    }
    private static string? StringProperty(JsonElement source, string name) => source.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static AiServiceException Blocked() => new("blocked", "OpenAI chưa thể xử lý nội dung này theo bộ lọc của nhà cung cấp. Hãy kiểm tra nội dung rồi thử lại.");
}
