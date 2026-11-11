namespace AiInterview.Infrastructure.Identity;

/// <summary>Only protected credentials are persisted. No navigation exposes this row through user views.</summary>
public sealed class UserAiSettings
{
    public string UserId { get; set; } = "";
    public bool UsePersonalSettings { get; set; }
    public string Provider { get; set; } = "Gemini";
    public string? ProtectedGeminiApiKey { get; set; }
    public string? ProtectedOpenAiApiKey { get; set; }
    public string GeminiModel { get; set; } = "gemini-3.1-flash-lite";
    public string OpenAiModel { get; set; } = "gpt-4.1-mini";
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
