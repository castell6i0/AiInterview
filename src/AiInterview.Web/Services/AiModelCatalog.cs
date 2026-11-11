namespace AiInterview.Web.Services;

public sealed record AiModelSuggestion(string Provider, string Id, string Name, bool HasFreeTier, string Description);

// Curated text models with structured output. These are suggestions, not an account's live entitlements.
// Pricing checked 2026-10-16: https://ai.google.dev/gemini-api/docs/pricing
// Only models with a documented free tier belong in the quick-pick list.
public static class AiModelCatalog
{
    public const string CheckedOn = "16/10/2026";
    public static IReadOnlyList<AiModelSuggestion> Models { get; } = Array.AsReadOnly<AiModelSuggestion>(
    [
        new("Gemini", "gemini-3.5-flash-lite", "Gemini 3.5 Flash-Lite", true, "Nhanh, tiết kiệm; phù hợp để bắt đầu luyện tập."),
        new("Gemini", "gemini-3.8-flash", "Gemini 3.8 Flash", true, "Có thể chọn cho câu hỏi và bài tập cần suy luận nhiều hơn."),
        new("Gemini", "gemini-3.6-flash", "Gemini 3.6 Flash", true, "Một lựa chọn Flash khác cho phỏng vấn và xử lý văn bản."),
        new("Gemini", "gemini-3.1-flash-lite", "Gemini 3.1 Flash-Lite", true, "Giữ lựa chọn Flash-Lite đã có trong các bản InterviewLab trước.")
    ]);
}
