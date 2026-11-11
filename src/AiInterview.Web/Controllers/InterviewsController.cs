using System.Security.Claims;
using System.Text.Json;
using AiInterview.Application.Interfaces;
using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiInterview.Web.Controllers;

[Authorize]
public sealed class InterviewsController(InterviewService interviews, AppDbContext db, IAiService ai) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Create(int? assignmentId, string? focusSkills, int? cvProfileId, int? jobDescriptionId, InterviewKind? kind, CancellationToken ct)
    {
        var model = new CreateInterviewModel { AssignmentId = assignmentId, FocusSkills = focusSkills ?? "", CvProfileId = cvProfileId, JobDescriptionId = jobDescriptionId, Kind = kind is not null && Enum.IsDefined(kind.Value) ? kind.Value : InterviewKind.Technical };
        model.ConsentToAi = false;
        ModelState.Remove(nameof(model.ConsentToAi));
        ModelState.Remove(nameof(model.ConsentedProvider));
        await interviews.PopulateAsync(model, UserId, ct); return View(model);
    }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Create(CreateInterviewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try { var id = await interviews.CreateAsync(UserId, model, ct); return RedirectToAction(nameof(Room), new { id }); }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        // A failed submission can now show a different provider. Require a fresh confirmation.
        model.ConsentToAi = false;
        ModelState.Remove(nameof(model.ConsentToAi));
        ModelState.Remove(nameof(model.ConsentedProvider));
        await interviews.PopulateAsync(model, UserId, ct); return View(model);
    }
    [Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Room(Guid id, CancellationToken ct)
    {
        var session = await interviews.GetOwnedAsync(UserId, id, ct);
        if (session.Status == SessionStatus.Completed) return RedirectToAction(nameof(Report), new { id });
        return View(new RoomModel { Session = session, AiConfigured = ai.IsConfigured });
    }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> ConsentProvider(Guid id, string provider, bool consent, CancellationToken ct)
    {
        if (!consent) { TempData["ProviderError"] = "Hãy xác nhận nhà cung cấp nhận dữ liệu trước khi tiếp tục."; return RedirectToAction(nameof(Room), new { id }); }
        try { await interviews.ConsentToProviderAsync(UserId, id, provider, ct); }
        catch (InvalidOperationException ex) { TempData["ProviderError"] = ex.Message; }
        return RedirectToAction(nameof(Room), new { id });
    }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Student)]
    public async Task Stream(Guid id, string operation, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream"; Response.Headers.CacheControl = "no-cache, no-store"; Response.Headers["X-Accel-Buffering"] = "no";
        async Task Send(string name, object value)
        {
            await Response.WriteAsync($"event: {name}\ndata: {JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        try
        {
            if (operation == "evaluate")
            {
                await interviews.EvaluateAsync(UserId, id, text => Send("status", new { text }), ct);
                await Send("complete", new { reportUrl = Url.Action(nameof(Report), new { id }) });
            }
            else if (operation == "question")
            {
                await Send("status", new { text = "AI đang nghĩ…" });
                var turn = await interviews.GenerateNextAsync(UserId, id, chunk => Send(chunk.Length == 0 ? "reset" : "delta", new { text = chunk }), ct);
                if (turn is null) await Send("ready", new { text = "Bạn đã hoàn tất các câu hỏi. Hãy xem báo cáo." });
                else await Send("question", new { turn.Id, turn.Question, turn.SequenceNumber, turn.MainQuestionNumber, turn.FollowUpDepth, turn.IsCoding, turn.CodeLanguage, answer = turn.DraftAnswer ?? "" });
            }
            else await Send("error", new { message = "Thao tác không hợp lệ." });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (AiServiceException ex) { await Send("error", new { message = ex.Message, code = ex.Code,
            retryAfterSeconds = ex.Code is "quota" or "rate_limit" or "system_concurrency" ? 60 : 0 }); }
        catch (KeyNotFoundException) { await Send("error", new { message = "Không tìm thấy buổi phỏng vấn." }); }
        catch (InvalidOperationException ex) { await Send("error", new { message = ex.Message }); }
        catch (Exception) { await Send("error", new { message = "Chưa thể hoàn tất xử lý. Dữ liệu đã lưu được giữ lại, hãy thử lại.", code = "server_error" }); }
    }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Answer(Guid id, [FromBody] SubmitAnswerModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Câu trả lời hoặc dữ liệu giọng nói không hợp lệ." });
        try { await interviews.AnswerAsync(UserId, id, model, ct); return Ok(new { saved = true }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Draft(Guid id, [FromBody] DraftAnswerModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest();
        await interviews.DraftAsync(UserId, id, model, ct); return Ok(new { saved = true });
    }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> End(Guid id, CancellationToken ct)
    {
        try { await interviews.EndAsync(UserId, id, ct); return Ok(new { saved = true }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
    public async Task<IActionResult> Report(Guid id, CancellationToken ct)
    {
        var isLecturer = User.IsInRole(Roles.Lecturer);
        // Authorize with a scalar projection before loading turns, snapshots and evaluations.
        var owner = await db.Sessions.Where(s => s.Id == id && (s.StudentId == UserId || isLecturer &&
            db.Classes.Any(c => c.LecturerId == UserId && c.Members.Any(m => m.StudentId == s.StudentId))))
            .Select(s => s.StudentId).SingleOrDefaultAsync(ct);
        if (owner is null) return NotFound();
        // Repeat the predicate on the full read so revoking membership between reads is safe.
        var session = await interviews.FullSessions().AsNoTracking().SingleOrDefaultAsync(s => s.Id == id &&
            (s.StudentId == UserId || isLecturer && db.Classes.Any(c => c.LecturerId == UserId && c.Members.Any(m => m.StudentId == s.StudentId))), ct);
        if (session is null) return NotFound();
        if (session.Evaluation is null)
        {
            if (session.StudentId == UserId) return RedirectToAction(nameof(Room), new { id });
            return View("NotEvaluated", session);
        }
        var comments = db.LecturerComments.Where(c => c.SessionId == id);
        if (session.StudentId != UserId) comments = comments.Where(c => db.Classes.Any(k => k.Id == c.ClassId && k.LecturerId == UserId));
        return View(new ReportModel { Session = session, Usage = session.StudentId == UserId ? await db.AiRequestLogs.Where(x => x.SessionId == id).ToListAsync(ct) : [], Comments = await comments.OrderBy(c => c.CreatedAt).ToListAsync(ct) });
    }
    [Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Replay(Guid id, CancellationToken ct) => View(await interviews.GetOwnedAsync(UserId, id, ct));
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Mark(Guid id, MarkTurnModel model, CancellationToken ct)
    {
        await db.Turns.Where(t => t.Id == model.TurnId && t.SessionId == id && t.Session.StudentId == UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Starred, model.Starred).SetProperty(t => t.NeedsPractice, model.NeedsPractice), ct);
        return RedirectToAction(nameof(Replay), new { id });
    }
    [Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Compare(Guid left, Guid right, CancellationToken ct)
    {
        if (left == right) { TempData["Message"] = "Chọn hai buổi khác nhau để so sánh."; return RedirectToAction("History", "Dashboard"); }
        return View(new CompareModel { Left = await interviews.GetOwnedAsync(UserId, left, ct), Right = await interviews.GetOwnedAsync(UserId, right, ct) });
    }
}
