using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AiInterview.Application.Interfaces;

namespace AiInterview.Infrastructure.AI;

public sealed record AiProbeUsage(int? InputTokens, int? OutputTokens, int? ThinkingTokens);

/// <summary>A bounded generation check containing no user, CV, or interview data.</summary>
public static class AiConnectionProbe
{
    private const int MaxResponseCharacters = 64 * 1024;
    private const string Instruction = "This is a connection check. Return exactly the JSON object {\"ready\":true}.";
    private static readonly object Schema = new
    {
        type = "object",
        properties = new { ready = new { type = "boolean" } },
        required = new[] { "ready" },
        additionalProperties = false
    };

    // Mirrors the structured-output and streaming options used for interview questions.
    // The credential is added separately by the request-local configuration, never in the body.
    public static HttpContent CreateContent(string provider, string model) => provider == "OpenAI"
        ? JsonContent.Create(new
        {
            model,
            instructions = Instruction,
            input = new[] { new { role = "user", content = new[] { new { type = "input_text", text = "Check the connection." } } } },
            text = new { format = new { type = "json_schema", name = "connection_check", strict = true, schema = Schema } },
            store = false,
            stream = true,
            max_output_tokens = 512
        })
        : JsonContent.Create(new
        {
            systemInstruction = new { parts = new[] { new { text = Instruction } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = "Check the connection." } } } },
            generationConfig = new
            {
                responseFormat = new { text = new { mimeType = "APPLICATION_JSON", schema = Schema } },
                maxOutputTokens = 512
            }
        });

    public static async Task<AiProbeUsage> VerifyResponseAsync(string provider, HttpResponseMessage response, CancellationToken ct)
    {
        var body = await ReadBoundedAsync(await response.Content.ReadAsStreamAsync(ct), ct);
        if (!response.IsSuccessStatusCode)
            throw provider == "OpenAI"
                ? OpenAiProtocol.Error(response.StatusCode, body)
                : GeminiProtocol.Error(response.StatusCode, body);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        var openAiUsage = new OpenAiUsage();
        var geminiUsage = new GeminiUsage();
        var content = provider == "OpenAI"
            ? await OpenAiProtocol.ReadStreamAsync(stream, openAiUsage, _ => Task.CompletedTask, ct)
            : await GeminiAiService.ReadStreamAsync(stream, geminiUsage, _ => Task.CompletedTask, ct);
        // A complete-looking JSON chunk is not enough when the connection closes before
        // Gemini reports STOP. Parse provider failures above before checking completion.
        if (provider != "OpenAI" && !GeminiCompleted(body)) throw InvalidSample();
        if (!StructuredOutputValidator.IsValid(content, Schema)) throw InvalidSample();
        using var generated = JsonDocument.Parse(content);
        if (!generated.RootElement.GetProperty("ready").GetBoolean()) throw InvalidSample();
        return provider == "OpenAI" ? new(openAiUsage.Input, openAiUsage.Output, openAiUsage.Thinking)
            : new(geminiUsage.Input, geminiUsage.Output, geminiUsage.Thinking);
    }

    private static bool GeminiCompleted(string body)
    {
        using var reader = new StringReader(body);
        var data = new StringBuilder();
        var completed = false;
        void InspectEvent()
        {
            if (data.Length == 0) return;
            var payload = data.ToString();
            data.Clear();
            if (payload.Trim() == "[DONE]") return;
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array
                && candidates.GetArrayLength() > 0 && candidates[0].ValueKind == JsonValueKind.Object
                && candidates[0].TryGetProperty("finishReason", out var reason) && reason.ValueKind == JsonValueKind.String
                && reason.GetString() == "STOP") completed = true;
        }
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) { InspectEvent(); continue; }
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var value = line[5..];
            if (value.StartsWith(' ')) value = value[1..];
            if (data.Length > 0) data.Append('\n');
            data.Append(value);
        }
        InspectEvent();
        return completed;
    }

    private static async Task<string> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var content = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (content.Length + count > MaxResponseCharacters) throw InvalidSample();
            content.Append(buffer, 0, count);
        }
        return content.ToString();
    }

    private static AiServiceException InvalidSample() => new("invalid_response",
        "AI chưa tạo được nội dung mẫu đúng định dạng. Hãy thử kiểm tra lại hoặc chọn model khác trong Cài đặt AI.");
}
