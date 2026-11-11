using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AiInterview.Infrastructure.AI;
using AiInterview.Infrastructure.Identity;
using AiInterview.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiInterview.Web.Services;

// This is the only settings type intended for the controller/view. It never contains a credential.
public sealed record UserAiSettingsState(bool UsePersonalSettings, string Provider,
    string GeminiModel, string OpenAiModel, bool HasGeminiKey, bool HasOpenAiKey,
    bool GeminiKeyUnavailable, bool OpenAiKeyUnavailable, DateTimeOffset? UpdatedAt);

public sealed class UserAiSettingsValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public sealed class UserAiSettingsService(AppDbContext db, IDataProtectionProvider protection,
    IOptions<AiProviderOptions> providerOptions, IOptions<GeminiOptions> geminiOptions,
    IOptions<OpenAiOptions> openAiOptions, TimeProvider clock)
{
    public async Task<UserAiSettingsState> GetAsync(string userId, CancellationToken ct = default)
    {
        var row = await ReadAsync(userId, ct);
        var gemini = ReadKey(row, "Gemini");
        var openAi = ReadKey(row, "OpenAI");
        return new(row?.UsePersonalSettings ?? false, row?.Provider ?? providerOptions.Value.Provider,
            row?.GeminiModel ?? geminiOptions.Value.Model, row?.OpenAiModel ?? openAiOptions.Value.Model,
            gemini.Key.Length > 0, openAi.Key.Length > 0, gemini.Unavailable, openAi.Unavailable, row?.UpdatedAt);
    }

    public async Task SaveAsync(string userId, string provider, string? apiKey, string model, CancellationToken ct = default)
    {
        provider = ValidateProvider(provider);
        model = ValidateModel(model);
        apiKey = ValidateApiKey(apiKey);
        var row = await GetOrCreateAsync(userId, ct);
        if (apiKey.Length == 0 && ReadKey(row, provider).Key.Length == 0)
            throw new UserAiSettingsValidationException("ApiKey", "Hãy nhập API key của nhà cung cấp đã chọn.");
        if (apiKey.Length > 0)
        {
            var encrypted = Protector(userId, provider).Protect(apiKey);
            if (provider == "Gemini") row.ProtectedGeminiApiKey = encrypted;
            else row.ProtectedOpenAiApiKey = encrypted;
        }
        if (provider == "Gemini") row.GeminiModel = model;
        else row.OpenAiModel = model;
        row.Provider = provider;
        row.UsePersonalSettings = true;
        await SaveChangesAsync(row, ct);
    }

    public async Task DeleteKeyAsync(string userId, string provider, CancellationToken ct = default)
    {
        provider = ValidateProvider(provider);
        RequireUserId(userId);
        var row = await db.UserAiSettings.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is null) return;
        if (provider == "Gemini") row.ProtectedGeminiApiKey = null;
        else row.ProtectedOpenAiApiKey = null;
        // Removing the selected personal key must not silently spend the server's credentials.
        await SaveChangesAsync(row, ct);
    }

    public async Task UseServerDefaultsAsync(string userId, CancellationToken ct = default)
    {
        RequireUserId(userId);
        var row = await db.UserAiSettings.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is null) return;
        row.UsePersonalSettings = false;
        await SaveChangesAsync(row, ct);
    }

    internal async Task<UserAiSettings?> ReadAsync(string userId, CancellationToken ct)
    {
        RequireUserId(userId);
        return await db.UserAiSettings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
    }

    internal (string Key, bool Unavailable) ReadKey(UserAiSettings? row, string provider)
    {
        var encrypted = provider == "Gemini" ? row?.ProtectedGeminiApiKey : row?.ProtectedOpenAiApiKey;
        if (string.IsNullOrEmpty(encrypted)) return ("", false);
        try
        {
            var key = Protector(row!.UserId, provider).Unprotect(encrypted);
            if (key.Length is < 1 or > 2048 || key.Any(c => c < '!' || c > '~')) return ("", true);
            return (key, false);
        }
        catch (CryptographicException) { return ("", true); }
        catch (FormatException) { return ("", true); }
    }

    private IDataProtector Protector(string userId, string provider) =>
        protection.CreateProtector("InterviewLab.PersonalAiCredential.v1", userId, provider);

    private async Task<UserAiSettings> GetOrCreateAsync(string userId, CancellationToken ct)
    {
        RequireUserId(userId);
        var row = await db.UserAiSettings.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is not null) return row;
        row = new UserAiSettings
        {
            UserId = userId, Provider = providerOptions.Value.Provider,
            GeminiModel = geminiOptions.Value.Model, OpenAiModel = openAiOptions.Value.Model
        };
        db.UserAiSettings.Add(row);
        return row;
    }

    private async Task SaveChangesAsync(UserAiSettings row, CancellationToken ct)
    {
        row.UpdatedAt = clock.GetUtcNow();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw ChangedElsewhere();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw ChangedElsewhere();
        }
    }

    private static UserAiSettingsValidationException ChangedElsewhere() => new("",
        "Cấu hình vừa được thay đổi ở một cửa sổ khác. Hãy tải lại trang và thử lại.");

    private static void RequireUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A signed-in user is required.", nameof(userId));
    }

    public static string ValidateProvider(string? provider) => provider?.Trim() switch
    {
        "Gemini" => "Gemini", "OpenAI" => "OpenAI",
        _ => throw new UserAiSettingsValidationException("Provider", "Hãy chọn Gemini hoặc OpenAI.")
    };

    public static string ValidateModel(string? model)
    {
        model = model?.Trim() ?? "";
        if (!Regex.IsMatch(model, @"\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,99}\z"))
            throw new UserAiSettingsValidationException("Model", "Tên model cần bắt đầu bằng chữ hoặc số, tối đa 100 ký tự; chỉ dùng chữ, số, dấu chấm, gạch ngang và gạch dưới.");
        return model;
    }

    public static string ValidateApiKey(string? apiKey)
    {
        apiKey = apiKey?.Trim() ?? "";
        if (apiKey.Length > 2048 || apiKey.Any(c => c < '!' || c > '~'))
            throw new UserAiSettingsValidationException("ApiKey", "API key không hợp lệ. Hãy dán lại key, không kèm khoảng trắng hoặc xuống dòng ở giữa.");
        return apiKey;
    }
}
