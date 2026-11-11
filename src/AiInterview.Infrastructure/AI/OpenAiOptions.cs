namespace AiInterview.Infrastructure.AI;

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4.1-mini";
    public int MaxRequestsPerMinute { get; set; } = 20;
    public int TimeoutSeconds { get; set; } = 90;
    public int MaxRetries { get; set; } = 2;
    public int MaxOutputTokens { get; set; } = 16384;
    public decimal? InputPricePerMillionTokensUsd { get; set; }
    public decimal? OutputPricePerMillionTokensUsd { get; set; }
    public TimeSpan MaximumCallDuration => TimeSpan.FromSeconds(
        (Math.Clamp(MaxRetries, 0, 3) + 1) * (Math.Clamp(TimeoutSeconds, 10, 300) + 70)
        + Math.Clamp(MaxRetries, 0, 3) * 10 + 60);
}
