using System.ComponentModel.DataAnnotations;

namespace AiInterview.Web.ViewModels;

// This page model deliberately has no credential property.
public sealed class AiSettingsViewModel
{
    public string Provider { get; set; } = "Gemini";
    public string Model { get; set; } = "";
    public bool HasGeminiKey { get; set; }
    public bool HasOpenAiKey { get; set; }
    public bool GeminiKeyUnavailable { get; set; }
    public bool OpenAiKeyUnavailable { get; set; }
    public bool UsePersonalSettings { get; set; }
    public bool IsConfigured { get; set; }
    public bool ServerIsConfigured { get; set; }
    public string GeminiModel { get; set; } = "";
    public string OpenAiModel { get; set; } = "";
    public string EffectiveProvider { get; set; } = "";
    public string EffectiveModel { get; set; } = "";
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? ReturnUrl { get; set; }
    public string ContinueUrl { get; set; } = "/";
}

public sealed class SaveAiSettingsInput
{
    public string? ReturnUrl { get; set; }
    public bool CheckConnection { get; set; }
    [Required(ErrorMessage = "Hãy chọn Gemini hoặc OpenAI.")]
    [RegularExpression("^(Gemini|OpenAI)$", ErrorMessage = "Hãy chọn Gemini hoặc OpenAI.")]
    public string Provider { get; set; } = "";

    [Required(ErrorMessage = "Hãy nhập tên model.")]
    [StringLength(100, ErrorMessage = "Tên model tối đa 100 ký tự.")]
    public string Model { get; set; } = "";

    [StringLength(2048, ErrorMessage = "API key quá dài. Vui lòng kiểm tra lại.")]
    public string? ApiKey { get; set; }
}
