using System.Security.Claims;
using AiInterview.Domain.Entities;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiInterview.Web.Controllers;

[Authorize(Roles = Roles.Lecturer)]
public sealed class LecturerController(LecturerService lecturers) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet] public async Task<IActionResult> Index() => View(await lecturers.ListAsync(UserId));
    [HttpGet] public IActionResult Create() => View(new CreateClassVm());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateClassVm model)
    {
        if (ModelState.IsValid)
        {
            try { var id = await lecturers.CreateAsync(UserId, model.Name); return RedirectToAction(nameof(Details), new { id }); }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        return View(model);
    }

    [HttpGet] public async Task<IActionResult> Details(int id)
    {
        var model = await lecturers.DetailAsync(id, UserId);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet] public async Task<IActionResult> Student(int classId, string studentId)
    {
        var model = await lecturers.StudentAsync(classId, studentId, UserId);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool active)
    {
        try { await lecturers.SetActiveAsync(id, UserId, active); TempData["ClassMessage"] = active ? "Đã mở lớp." : "Đã đóng lớp. Lịch sử và bài nộp vẫn được giữ."; }
        catch (InvalidOperationException ex) { TempData["ClassError"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet] public async Task<IActionResult> CreateAssignment(int classId)
    {
        var model = await lecturers.NewAssignmentAsync(classId, UserId);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAssignment(AssignmentEditVm model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await lecturers.CreateAssignmentAsync(model, UserId);
                TempData["ClassMessage"] = "Đã giao bài cho lớp; cấu hình và rubric được giữ cố định để chấm nhất quán.";
                return RedirectToAction(nameof(Details), new { id = model.ClassId });
            }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        var available = await lecturers.NewAssignmentAsync(model.ClassId, UserId);
        if (available is null) return NotFound();
        model.Positions = available.Positions;
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Kick(int classId, string studentId)
    {
        try { await lecturers.KickAsync(classId, studentId, UserId); TempData["ClassMessage"] = "Đã mời sinh viên ra khỏi lớp. Sinh viên không thể dùng mã để vào lại; bài đã nộp vẫn được giữ."; }
        catch (InvalidOperationException ex) { TempData["ClassError"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id = classId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Comment(LecturerCommentVm model)
    {
        if (!ModelState.IsValid) { TempData["ClassError"] = "Nhận xét cần có nội dung, tối đa 3.000 ký tự."; return RedirectToAction(nameof(Details), new { id = model.ClassId }); }
        try
        {
            var studentId = await lecturers.AddCommentAsync(model, UserId);
            TempData["ClassMessage"] = "Đã lưu nhận xét cho sinh viên.";
            if (await lecturers.StudentAsync(model.ClassId, studentId, UserId) is null) return RedirectToAction(nameof(Details), new { id = model.ClassId });
            return RedirectToAction(nameof(Student), new { classId = model.ClassId, studentId });
        }
        catch (InvalidOperationException ex) { TempData["ClassError"] = ex.Message; return RedirectToAction(nameof(Details), new { id = model.ClassId }); }
    }

    [HttpGet] public async Task<IActionResult> Export(int id)
    {
        var bytes = await lecturers.ExportCsvAsync(id, UserId);
        return bytes is null ? NotFound() : File(bytes, "text/csv; charset=utf-8", $"lop-{id}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
