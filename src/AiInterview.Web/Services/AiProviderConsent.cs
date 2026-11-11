namespace AiInterview.Web.Services;

public static class AiProviderConsent
{
    public static void RequireCurrent(string? consentedProvider, string currentProvider)
    {
        if (consentedProvider is not ("Gemini" or "OpenAI")
            || !string.Equals(consentedProvider, currentProvider, StringComparison.Ordinal))
            throw new InvalidOperationException("Nhà cung cấp AI đã thay đổi hoặc chưa được xác nhận. Hãy tải lại trang và đồng ý với nhà cung cấp đang hiển thị trước khi tiếp tục; dữ liệu của bạn vẫn được giữ.");
    }
}
