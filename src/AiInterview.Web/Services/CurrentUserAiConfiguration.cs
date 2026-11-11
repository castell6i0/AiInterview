using System.Net.Http.Headers;
using AiInterview.Application.Interfaces;
using AiInterview.Infrastructure.AI;
using AiInterview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiInterview.Web.Services;

/// <summary>A request-local, once-resolved snapshot. Provider options are always private copies.</summary>
public sealed class CurrentUserAiConfiguration(UserAiSettingsService settings,
    IOptions<AiProviderOptions> serverProvider, IOptions<GeminiOptions> serverGemini,
    IOptions<OpenAiOptions> serverOpenAi)
{
    private GeminiOptions gemini = Clone(serverGemini.Value);
    private OpenAiOptions openAi = Clone(serverOpenAi.Value);
    private bool resolved;
    private string? resolvedUserId;
    public bool UsePersonalSettings { get; private set; }
    internal string? UserId => resolvedUserId;
    internal int MaxRequestsPerMinute => Provider == "OpenAI" ? openAi.MaxRequestsPerMinute : gemini.MaxRequestsPerMinute;
    public string Provider { get; private set; } = serverProvider.Value.Provider;
    public string Model => Provider == "OpenAI" ? openAi.Model : gemini.Model;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Provider == "OpenAI" ? openAi.ApiKey : gemini.ApiKey);

    public async Task ResolveAsync(string? userId, CancellationToken ct = default)
    {
        userId = string.IsNullOrWhiteSpace(userId) ? null : userId;
        if (resolved)
        {
            // Production exception handling re-executes the pipeline in the same request scope.
            if (!string.Equals(resolvedUserId, userId, StringComparison.Ordinal))
                throw new InvalidOperationException("AI configuration cannot switch users within a request.");
            return;
        }
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var row = await settings.ReadAsync(userId, ct);
            if (row?.UsePersonalSettings == true)
            {
                UsePersonalSettings = true;
                Provider = row.Provider;
                // Overwrite both keys, including with empty values. Personal mode never falls back.
                gemini.ApiKey = settings.ReadKey(row, "Gemini").Key;
                gemini.Model = row.GeminiModel;
                openAi.ApiKey = settings.ReadKey(row, "OpenAI").Key;
                openAi.Model = row.OpenAiModel;
                // A personal project/model may use different billing from the shared key.
                // Unknown prices stay unknown; never inherit a server free-tier claim.
                gemini.IsFreeTier = false;
                gemini.InputPricePerMillionTokensUsd = gemini.OutputPricePerMillionTokensUsd = null;
                openAi.InputPricePerMillionTokensUsd = openAi.OutputPricePerMillionTokensUsd = null;
            }
        }
        resolvedUserId = userId;
        resolved = true;
    }

    public IAiService CreateService(IHttpClientFactory clients, DbContextOptions<AppDbContext> dbOptions,
        AiRequestLimiter limiter)
    {
        RequireResolved();
        return Provider == "OpenAI"
            ? new OpenAiService(clients.CreateClient(nameof(OpenAiService)), dbOptions, Options.Create(Clone(openAi)), limiter, !UsePersonalSettings)
            : new GeminiAiService(clients.CreateClient(nameof(GeminiAiService)), dbOptions, Options.Create(Clone(gemini)), limiter, !UsePersonalSettings);
    }

    internal async Task<CurrentUserAiConfiguration> ReadLatestSavedAsync(CancellationToken ct)
    {
        RequireResolved();
        ct.ThrowIfCancellationRequested();
        var latest = new CurrentUserAiConfiguration(settings, serverProvider, serverGemini, serverOpenAi);
        await latest.ResolveAsync(resolvedUserId, ct);
        return latest;
    }

    public HttpRequestMessage CreateConnectionProbeRequest()
    {
        RequireResolved();
        if (!IsConfigured) throw new UserAiSettingsValidationException("ApiKey", "Hãy lưu API key trước khi kiểm tra kết nối.");
        var model = UserAiSettingsService.ValidateModel(Model);
        var key = UserAiSettingsService.ValidateApiKey(Provider == "OpenAI" ? openAi.ApiKey : gemini.ApiKey);
        var request = new HttpRequestMessage(HttpMethod.Post, Provider == "OpenAI"
            ? "https://api.openai.com/v1/responses"
            : $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse");
        request.Content = AiConnectionProbe.CreateContent(Provider, model);
        if (Provider == "OpenAI") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        else request.Headers.Add("x-goog-api-key", key);
        return request;
    }

    private void RequireResolved()
    {
        if (!resolved) throw new InvalidOperationException("Resolve current user AI settings after authentication, before creating an AI service.");
    }

    private static GeminiOptions Clone(GeminiOptions value) => new()
    {
        ApiKey = value.ApiKey, Model = value.Model, MaxRequestsPerMinute = value.MaxRequestsPerMinute,
        TimeoutSeconds = value.TimeoutSeconds, MaxRetries = value.MaxRetries, MaxOutputTokens = value.MaxOutputTokens,
        IsFreeTier = value.IsFreeTier, InputPricePerMillionTokensUsd = value.InputPricePerMillionTokensUsd,
        OutputPricePerMillionTokensUsd = value.OutputPricePerMillionTokensUsd
    };

    private static OpenAiOptions Clone(OpenAiOptions value) => new()
    {
        ApiKey = value.ApiKey, Model = value.Model, MaxRequestsPerMinute = value.MaxRequestsPerMinute,
        TimeoutSeconds = value.TimeoutSeconds, MaxRetries = value.MaxRetries, MaxOutputTokens = value.MaxOutputTokens,
        InputPricePerMillionTokensUsd = value.InputPricePerMillionTokensUsd,
        OutputPricePerMillionTokensUsd = value.OutputPricePerMillionTokensUsd
    };
}
