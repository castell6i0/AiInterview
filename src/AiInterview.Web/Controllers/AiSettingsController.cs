using System.Security.Claims;
using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.AI;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AiInterview.Web.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiSettingsController(UserAiSettingsService settings, CurrentUserAiConfiguration current,
    AiConnectionCheckService connection, IConfiguration config, IOptions<GeminiOptions> gemini, IOptions<OpenAiOptions> openAi) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Bạn cần đăng nhập lại.");

    [HttpGet]
    public async Task<IActionResult> Index(string? provider, CancellationToken ct, string? returnUrl = null)
    {
        var saved = await settings.GetAsync(UserId, ct);
        var selected = provider is "Gemini" or "OpenAI" ? provider : saved.Provider;
        return View(new AiSettingsViewModel
        {
            Provider = selected, Model = selected == "OpenAI" ? saved.OpenAiModel : saved.GeminiModel,
            GeminiModel = saved.GeminiModel, OpenAiModel = saved.OpenAiModel,
            HasGeminiKey = saved.HasGeminiKey, HasOpenAiKey = saved.HasOpenAiKey,
            GeminiKeyUnavailable = saved.GeminiKeyUnavailable, OpenAiKeyUnavailable = saved.OpenAiKeyUnavailable,
            UsePersonalSettings = current.UsePersonalSettings, IsConfigured = current.IsConfigured,
            EffectiveProvider = current.Provider, EffectiveModel = current.Model,
            ServerIsConfigured = !string.IsNullOrWhiteSpace(config["AI:Provider"] == "OpenAI" ? openAi.Value.ApiKey : gemini.Value.ApiKey),
            ReturnUrl = SafeReturnUrl(returnUrl),
            ContinueUrl = SafeReturnUrl(returnUrl) ?? (User.IsInRole(Roles.Student) ? "/Interviews/Create" : "/"),
            UpdatedAt = saved.UpdatedAt, StatusMessage = TempData["AiSettingsStatus"] as string,
            ErrorMessage = TempData["AiSettingsError"] as string
        });
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16_384)]
    public async Task<IActionResult> Save(SaveAiSettingsInput input, CancellationToken ct)
    {
        var selected = input.Provider is "OpenAI" or "Gemini" ? input.Provider : null;
        TempData.Remove("AiSettingsStatus");
        TempData.Remove("AiSettingsError");
        try
        {
            if (!ModelState.IsValid)
            {
                TempData["AiSettingsError"] = "Thông tin chưa hợp lệ. Chọn Gemini/OpenAI, nhập model tối đa 100 ký tự và kiểm tra lại key. Key vừa nhập không được lưu; vui lòng nhập lại.";
                return RedirectToAction(nameof(Index), new { provider = selected, returnUrl = SafeReturnUrl(input.ReturnUrl) });
            }
            await settings.SaveAsync(UserId, input.Provider, input.ApiKey, input.Model, ct);
            if (input.CheckConnection)
            {
                // Middleware resolved the old settings before this POST saved the user's choice.
                var result = await connection.CheckLatestSavedAsync(ct);
                TempData[result.Succeeded ? "AiSettingsStatus" : "AiSettingsError"] = "Đã lưu cấu hình AI riêng. " + result.Message;
            }
            else
                TempData["AiSettingsStatus"] = "Đã lưu cấu hình AI riêng. Bấm Kiểm tra kết nối đã lưu để thử tạo nội dung bằng key và model này trước khi luyện tập.";
        }
        catch (UserAiSettingsValidationException ex) { TempData["AiSettingsError"] = ex.Message; }
        finally
        {
            // Never let a failed form echo the attempted password via MVC ModelState or TempData.
            input.ApiKey = null;
            ModelState.Clear();
        }
        return RedirectToAction(nameof(Index), new { provider = selected, returnUrl = SafeReturnUrl(input.ReturnUrl) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteKey(string provider, CancellationToken ct, string? returnUrl = null)
    {
        try
        {
            await settings.DeleteKeyAsync(UserId, provider, ct);
            TempData["AiSettingsStatus"] = "Đã xóa key riêng của dịch vụ đã chọn. Nếu đang dùng dịch vụ này, hãy nhập key mới hoặc chủ động chọn cấu hình hệ thống.";
        }
        catch (UserAiSettingsValidationException ex) { TempData["AiSettingsError"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { returnUrl = SafeReturnUrl(returnUrl) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UseServerDefaults(CancellationToken ct, string? returnUrl = null)
    {
        try
        {
            await settings.UseServerDefaultsAsync(UserId, ct);
            TempData["AiSettingsStatus"] = "Đã chuyển sang cấu hình AI của hệ thống. Key riêng đã lưu vẫn được giữ để bạn dùng lại.";
        }
        catch (UserAiSettingsValidationException ex) { TempData["AiSettingsError"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { returnUrl = SafeReturnUrl(returnUrl) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TestConnection(CancellationToken ct, string? returnUrl = null)
    {
        var result = await connection.CheckAsync(ct);
        TempData[result.Succeeded ? "AiSettingsStatus" : "AiSettingsError"] = result.Message;
        return RedirectToAction(nameof(Index), new { returnUrl = SafeReturnUrl(returnUrl) });
    }

    private string? SafeReturnUrl(string? value) =>
        value is { Length: > 0 and <= 2048 } && Url.IsLocalUrl(value) ? value : null;
}
