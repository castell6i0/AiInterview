using System.Security.Claims;
using AiInterview.Domain.Entities;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiInterview.Web.Controllers;

[Authorize(Roles = Roles.Student)]
public sealed class DashboardController(DashboardService dashboard, InterviewService interviews) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index(int? positionId, InterviewLanguage? language, InterviewLevel? level, CancellationToken ct) =>
        View(await dashboard.IndexAsync(UserId, positionId, language, level, ct));

    [HttpGet]
    public async Task<IActionResult> History([Bind("PositionId,Language,Status,From,To,Page")] HistoryModel input, CancellationToken ct)
    {
        if (input.From > input.To) { ModelState.AddModelError("", "Ngày bắt đầu phải trước hoặc bằng ngày kết thúc."); input.From = null; input.To = null; }
        return View(await dashboard.HistoryAsync(UserId, input, ct));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PracticeWeak([Bind(Prefix = "Practice")] PracticeWeakModel input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["DashboardError"] = "Chọn vị trí, cấp độ, ngôn ngữ và đồng ý gửi câu trả lời tới nhà cung cấp AI đã chọn.";
            return RedirectToAction(nameof(Index), new { positionId = input.PositionId, language = input.Language, level = input.Level });
        }
        try
        {
            var skills = await dashboard.WeakSkillsAsync(UserId, input.PositionId, input.Language, input.Level, ct);
            if (skills.Length == 0) throw new InvalidOperationException("Chưa có dữ liệu đã chấm cho lựa chọn này. Hãy hoàn thành một buổi trước để xác định chủ đề cần luyện.");
            var id = await interviews.CreateAsync(UserId, new CreateInterviewModel
            {
                PositionId = input.PositionId, Language = input.Language, Level = input.Level, FocusSkills = string.Join(',', skills),
                QuestionCount = 5, Kind = InterviewKind.Mixed, Mode = InterviewMode.Text, Persona = InterviewPersona.Friendly,
                ConsentToAi = input.Consent, ConsentedProvider = input.ConsentedProvider
            }, ct);
            return RedirectToAction("Room", "Interviews", new { id });
        }
        catch (InvalidOperationException ex) { TempData["DashboardError"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { positionId = input.PositionId, language = input.Language, level = input.Level });
    }
}
