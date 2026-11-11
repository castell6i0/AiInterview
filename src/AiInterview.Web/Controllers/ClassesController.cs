using System.Security.Claims;
using AiInterview.Domain.Entities;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiInterview.Web.Controllers;

[Authorize(Roles = Roles.Student)]
public sealed class ClassesController(ClassroomService classrooms) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet] public async Task<IActionResult> Index() => View(await classrooms.ListAsync(UserId));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Join([Bind(Prefix = "Join")] JoinClassVm model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var id = await classrooms.JoinAsync(UserId, model);
                TempData["ClassMessage"] = "Đã tham gia lớp. Giảng viên quản lý lớp có thể xem lịch sử, báo cáo và tiến bộ của bạn.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        var result = await classrooms.ListAsync(UserId); result.Join = model;
        return View(nameof(Index), result);
    }

    [HttpGet] public async Task<IActionResult> Details(int id)
    {
        var model = await classrooms.DetailAsync(id, UserId);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet] public async Task<IActionResult> Assignment(int id)
    {
        var model = await classrooms.AssignmentAsync(id, UserId);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int assignmentId, Guid sessionId)
    {
        try { await classrooms.SubmitAsync(assignmentId, sessionId, UserId); TempData["ClassMessage"] = "Đã nộp bài. Bạn có thể luyện và nộp lại bằng một buổi mới."; }
        catch (InvalidOperationException ex) { TempData["ClassError"] = ex.Message; }
        return RedirectToAction(nameof(Assignment), new { id = assignmentId });
    }
}
