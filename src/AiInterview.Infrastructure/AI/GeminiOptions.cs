namespace AiInterview.Infrastructure.AI;

public sealed class GeminiOptions
{
    public string ApiKey { get; set; } = "";
    // Used successfully in the live verification on 2026-10-11; availability and quotas remain account-specific.
    public string Model { get; set; } = "gemini-3.1-flash-lite";
    public int MaxRequestsPerMinute { get; set; } = 20;
    public int TimeoutSeconds { get; set; } = 90;
    public int MaxRetries { get; set; } = 2;
    public int MaxOutputTokens { get; set; } = 32768;
    public bool IsFreeTier { get; set; }
    public decimal? InputPricePerMillionTokensUsd { get; set; }
    public decimal? OutputPricePerMillionTokensUsd { get; set; }

    // Include each bounded HTTP attempt, two default 30s DB reservation commands, the 10s audit
    // update, capped retry delays and a final 60s margin. Invalid settings are rejected by the provider.
    public TimeSpan MaximumCallDuration => TimeSpan.FromSeconds(
        (Math.Clamp(MaxRetries, 0, 3) + 1) * (Math.Clamp(TimeoutSeconds, 10, 300) + 70)
        + Math.Clamp(MaxRetries, 0, 3) * 10 + 60);
}

