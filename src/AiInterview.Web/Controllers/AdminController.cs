using System.Security.Claims;
using AiInterview.Domain.Entities;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiInterview.Web.Controllers;

[Authorize(Roles = Roles.Admin)]
public sealed class AdminController(AdminService admin) : Controller
{
    [HttpGet] public async Task<IActionResult> Index() => View(await admin.IndexAsync());
    [HttpGet] public async Task<IActionResult> Users(string? search) => View(await admin.UsersAsync(search));

    [HttpGet] public IActionResult CreateUser() => View(new CreateUserVm());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(CreateUserVm model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await admin.CreateUserAsync(model);
                TempData["AdminMessage"] = "Đã tạo tài khoản với vai trò đã chọn.";
                return RedirectToAction(nameof(Users));
            }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        // Never redisplay the submitted password in the response.
        foreach (var field in new[] { nameof(model.Password), nameof(model.ConfirmPassword) })
            if (ModelState.TryGetValue(field, out var state))
                foreach (var error in state.Errors) ModelState.AddModelError("", error.ErrorMessage);
        model.Password = model.ConfirmPassword = "";
        ModelState.Remove(nameof(model.Password));
        ModelState.Remove(nameof(model.ConfirmPassword));
        return View(model);
    }

    [HttpGet] public async Task<IActionResult> EditUser(string id)
    {
        var model = await admin.UserAsync(id); return model is null ? NotFound() : View(model);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(EditUserVm model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await admin.EditUserAsync(model);
                TempData["AdminMessage"] = "Đã cập nhật họ tên và email. Người dùng cần đăng nhập lại bằng email đã lưu.";
                return RedirectToAction(nameof(Users));
            }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        return View(model);
    }

    [HttpGet] public async Task<IActionResult> DeleteUser(string id)
    {
        var model = await admin.DeletionAsync(id, User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return model is null ? NotFound() : View(model);
    }
    [HttpPost, ActionName(nameof(DeleteUser)), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUserConfirmed(string userId)
    {
        try
        {
            await admin.DeleteUserAsync(userId, User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            TempData["AdminMessage"] = "Đã xóa tài khoản chưa có dữ liệu lịch sử.";
        }
        catch (InvalidOperationException ex) { TempData["AdminError"] = ex.Message; }
        return RedirectToAction(nameof(Users));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UserAccess(UserAccessVm model)
    {
        if (!ModelState.IsValid) TempData["AdminError"] = "Thông tin quyền truy cập không hợp lệ.";
        else try { await admin.UpdateUserAsync(model, User.FindFirstValue(ClaimTypes.NameIdentifier)!); TempData["AdminMessage"] = "Đã cập nhật quyền truy cập và thu hồi phiên đăng nhập cũ."; }
            catch (InvalidOperationException ex) { TempData["AdminError"] = ex.Message; }
        return RedirectToAction(nameof(Users));
    }

    [HttpGet] public async Task<IActionResult> Positions() => View(await admin.PositionsAsync());
    [HttpGet] public async Task<IActionResult> Position(int id = 0)
    {
        var model = await admin.PositionAsync(id); return model is null ? NotFound() : View(model);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Position(PositionEditVm model)
    {
        if (ModelState.IsValid)
        {
            try { await admin.SavePositionAsync(model); TempData["AdminMessage"] = "Đã lưu vị trí."; return RedirectToAction(nameof(Positions)); }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        return View(model);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePosition(int id)
    {
        try { TempData["AdminMessage"] = await admin.DeletePositionAsync(id); }
        catch (InvalidOperationException ex) { TempData["AdminError"] = ex.Message; }
        return RedirectToAction(nameof(Positions));
    }

    [HttpGet] public async Task<IActionResult> Questions(string? search, int? positionId, int page = 1) => View(await admin.QuestionsAsync(search, positionId, page));
    [HttpGet] public async Task<IActionResult> Question(int id = 0)
    {
        var model = await admin.QuestionAsync(id); return model is null ? NotFound() : View(model);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Question(QuestionEditVm model)
    {
        if (ModelState.IsValid)
        {
            try { await admin.SaveQuestionAsync(model); TempData["AdminMessage"] = "Đã lưu câu hỏi và chủ đề kỹ năng."; return RedirectToAction(nameof(Questions)); }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        model.Positions = await admin.PositionsAsync();
        return View(model);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQuestion(int id)
    {
        try { await admin.DeleteQuestionAsync(id); TempData["AdminMessage"] = "Đã xóa câu hỏi khỏi ngân hàng. Các buổi cũ giữ nguyên câu hỏi đã hỏi."; }
        catch (InvalidOperationException ex) { TempData["AdminError"] = ex.Message; }
        return RedirectToAction(nameof(Questions));
    }

    [HttpGet] public async Task<IActionResult> Rubric() => View(await admin.RubricAsync());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Rubric(RubricEditVm model)
    {
        if (ModelState.IsValid)
        {
            try { await admin.SaveRubricAsync(model); TempData["AdminMessage"] = "Đã kích hoạt rubric mới. Điểm và rubric của các buổi/bài tập trước được giữ nguyên."; return RedirectToAction(nameof(Rubric)); }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        model.History = (await admin.RubricAsync()).History;
        return View(model);
    }
}
