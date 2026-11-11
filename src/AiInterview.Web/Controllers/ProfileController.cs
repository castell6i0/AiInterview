using System.Security.Claims;
using AiInterview.Application.Interfaces;
using AiInterview.Domain.Entities;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiInterview.Web.Controllers;

[Authorize(Roles = Roles.Student)]
public sealed class ProfileController(ProfileService profiles) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet] public async Task<IActionResult> Index(CancellationToken ct) => View(await profiles.IndexAsync(UserId, ct));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([Bind(Prefix = "Profile")] EditProfileModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try { await profiles.SaveProfileAsync(UserId, model, ct); TempData["ProfileMessage"] = "Đã lưu hồ sơ."; return RedirectToAction(nameof(Index)); }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        var page = await profiles.IndexAsync(UserId, ct); page.Profile = model;
        return View(nameof(Index), page);
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? cvFile, CancellationToken ct)
    {
        try
        {
            var id = await profiles.UploadAsync(UserId, cvFile, ct);
            TempData["ProfileMessage"] = "Đã trích xuất và lưu nội dung CV. Kiểm tra văn bản bên dưới trước khi chọn phân tích với AI.";
            return RedirectToAction(nameof(Cv), new { id });
        }
        catch (CvExtractionException ex) { TempData["ProfileError"] = ex.Message; return RedirectToAction(nameof(Index)); }
    }

    [HttpGet] public async Task<IActionResult> Cv(int id, CancellationToken ct)
    {
        try { return View(await profiles.CvAsync(UserId, id, ct)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AnalyzeCv(int id, AiConsentModel input, CancellationToken ct)
    {
        if (!ModelState.IsValid) { TempData["ProfileError"] = "Hãy tải lại trang và đồng ý gửi CV tới nhà cung cấp AI đang hiển thị trước khi phân tích."; return RedirectToAction(nameof(Cv), new { id }); }
        try { await profiles.AnalyzeCvAsync(UserId, id, input.Consent, input.ConsentedProvider, ct); TempData["ProfileMessage"] = "Đã phân tích kỹ năng, dự án và kinh nghiệm trong CV."; }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (AiServiceException ex) { TempData["ProfileError"] = ex.Message + " Nội dung CV đã lưu; bạn có thể thử lại."; TempData["ProfileAiSettingsNeeded"] = CanFixConnection(ex.Code); }
        catch (InvalidOperationException ex) { TempData["ProfileError"] = ex.Message; }
        return RedirectToAction(nameof(Cv), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCv(int id, CancellationToken ct)
    {
        try { await profiles.DeleteCvAsync(UserId, id, ct); TempData["ProfileMessage"] = "Đã xóa CV chưa sử dụng."; }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { TempData["ProfileError"] = ex.Message; return RedirectToAction(nameof(Cv), new { id }); }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet] public IActionResult CreateJd() => View(new CreateJdModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateJd(CreateJdModel input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(input);
        try { var id = await profiles.CreateJdAsync(UserId, input, ct); TempData["ProfileMessage"] = "Đã lưu JD. Bạn có thể so sánh với CV hoặc tạo buổi phỏng vấn."; return RedirectToAction(nameof(Jd), new { id }); }
        catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); return View(input); }
    }

    [HttpGet] public async Task<IActionResult> Jd(int id, CancellationToken ct)
    {
        try { return View(await profiles.JdAsync(UserId, id, ct)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CompareGap(CompareGapModel input, CancellationToken ct)
    {
        if (!ModelState.IsValid) { TempData["ProfileError"] = "Hãy tải lại trang, chọn CV và đồng ý gửi CV/JD tới nhà cung cấp AI đang hiển thị trước khi so sánh."; return RedirectToAction(nameof(Jd), new { id = input.JdId }); }
        try { await profiles.CompareGapAsync(UserId, input, ct); TempData["ProfileMessage"] = "Đã so sánh CV với yêu cầu tuyển dụng."; }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (AiServiceException ex) { TempData["ProfileError"] = ex.Message + " CV và JD vẫn được lưu; bạn có thể thử lại."; TempData["ProfileAiSettingsNeeded"] = CanFixConnection(ex.Code); }
        catch (InvalidOperationException ex) { TempData["ProfileError"] = ex.Message; }
        return RedirectToAction(nameof(Jd), new { id = input.JdId });
    }

    private static bool CanFixConnection(string code) => code is "not_configured" or "authentication" or "model_unavailable" or "quota" or "quota_daily" or "quota_exhausted" or "request_too_large" or "invalid_request" or "configuration";

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteJd(int id, CancellationToken ct)
    {
        try { await profiles.DeleteJdAsync(UserId, id, ct); TempData["ProfileMessage"] = "Đã xóa JD chưa sử dụng."; }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { TempData["ProfileError"] = ex.Message; return RedirectToAction(nameof(Jd), new { id }); }
        return RedirectToAction(nameof(Index));
    }
}
