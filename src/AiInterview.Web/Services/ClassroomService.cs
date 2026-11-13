using System.Data;
using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AiInterview.Web.Services;

public sealed class ClassroomService(AppDbContext db)
{
    public async Task<ClassListVm> ListAsync(string studentId) => new()
    {
        Classes = await db.Classes.AsNoTracking().Where(c => c.Members.Any(m => m.StudentId == studentId))
            .OrderByDescending(c => c.CreatedAt).ToListAsync()
    };

    public async Task<int> JoinAsync(string studentId, JoinClassVm model)
    {
        if (!model.AcceptSharing) throw new InvalidOperationException("Vui lòng xác nhận quyền xem lịch sử của giảng viên trước khi tham gia.");
        var code = model.Code.Trim().ToUpperInvariant();
        var classroom = await db.Classes.SingleOrDefaultAsync(c => c.JoinCode == code && c.IsActive)
            ?? throw new InvalidOperationException("Mã lớp không đúng hoặc lớp đã đóng.");
        if (await db.ClassBans.AnyAsync(b => b.ClassId == classroom.Id && b.StudentId == studentId))
            throw new InvalidOperationException("Giảng viên đã mời bạn ra khỏi lớp này nên bạn không thể tham gia lại.");
        if (await db.ClassMembers.AnyAsync(m => m.ClassId == classroom.Id && m.StudentId == studentId)) return classroom.Id;
        db.ClassMembers.Add(new ClassMember { ClassId = classroom.Id, StudentId = studentId });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.ClassMembers.AnyAsync(m => m.ClassId == classroom.Id && m.StudentId == studentId)) throw;
        }
        return classroom.Id;
    }

    public async Task<StudentClassVm?> DetailAsync(int classId, string studentId)
    {
        var classroom = await db.Classes.AsNoTracking().SingleOrDefaultAsync(c => c.Id == classId && c.Members.Any(m => m.StudentId == studentId));
        if (classroom is null) return null;
        return new StudentClassVm
        {
            Class = classroom,
            Assignments = await db.Assignments.AsNoTracking().Include(a => a.Position).Where(a => a.ClassId == classId).OrderByDescending(a => a.CreatedAt).ToListAsync(),
            Submissions = await db.AssignmentSubmissions.AsNoTracking().Include(s => s.Session).ThenInclude(s => s.Evaluation)
                .Where(s => s.StudentId == studentId && s.Assignment.ClassId == classId).OrderByDescending(s => s.SubmittedAt).ToListAsync()
        };
    }

    public async Task<AssignmentDetailVm?> AssignmentAsync(int assignmentId, string studentId)
    {
        var assignment = await db.Assignments.AsNoTracking().Include(a => a.Class).Include(a => a.Position)
            .SingleOrDefaultAsync(a => a.Id == assignmentId && a.Class.Members.Any(m => m.StudentId == studentId));
        if (assignment is null) return null;
        var submitted = await db.AssignmentSubmissions.AsNoTracking().Include(s => s.Session).ThenInclude(s => s.Evaluation)
            .Where(s => s.AssignmentId == assignmentId && s.StudentId == studentId).OrderByDescending(s => s.AttemptNumber).ToListAsync();
        return new AssignmentDetailVm
        {
            Assignment = assignment,
            Submissions = submitted,
            EligibleSessions = await EligibleSessions(studentId, assignment).Include(s => s.Position).Include(s => s.Evaluation)
                .Where(s => !db.AssignmentSubmissions.Any(previous => previous.SessionId == s.Id)).OrderByDescending(s => s.CreatedAt).ToListAsync()
        };
    }

    private IQueryable<InterviewSession> EligibleSessions(string studentId, Assignment assignment) => db.Sessions.AsNoTracking().Where(s =>
        s.StudentId == studentId && s.Status == SessionStatus.Completed && s.Evaluation != null && !s.Evaluation.IsProvisional
        && !s.EndedEarly && s.CreatedAt >= assignment.CreatedAt && s.PositionId == assignment.PositionId
        && s.Level == assignment.Level && s.Language == assignment.Language && s.Kind == assignment.Kind
        && s.PlannedQuestionCount == assignment.QuestionCount && s.RubricVersionId == assignment.RubricVersionId
        && s.Turns.Count(t => t.FollowUpDepth == 0 && t.Status == TurnStatus.Answered) == assignment.QuestionCount);

    public async Task SubmitAsync(int assignmentId, Guid sessionId, string studentId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var assignment = await db.Assignments.SingleOrDefaultAsync(a => a.Id == assignmentId
            && a.Class.IsActive && a.Class.Members.Any(m => m.StudentId == studentId))
            ?? throw new InvalidOperationException("Không tìm thấy bài tập đang mở trong lớp của bạn.");
        if (!await EligibleSessions(studentId, assignment).AnyAsync(s => s.Id == sessionId))
            throw new InvalidOperationException("Buổi chưa đủ điều kiện: cần hoàn thành đầy đủ, đúng cấu hình và rubric, được tạo sau khi giao bài.");
        if (await db.AssignmentSubmissions.AnyAsync(s => s.SessionId == sessionId))
            throw new InvalidOperationException("Buổi phỏng vấn này đã được nộp. Mỗi buổi chỉ nộp cho một bài tập một lần.");
        var previousAttempt = await db.AssignmentSubmissions.Where(s => s.AssignmentId == assignmentId && s.StudentId == studentId)
            .MaxAsync(s => (int?)s.AttemptNumber) ?? 0;
        var now = DateTimeOffset.UtcNow;
        db.AssignmentSubmissions.Add(new AssignmentSubmission
        {
            AssignmentId = assignmentId, StudentId = studentId, SessionId = sessionId,
            AttemptNumber = previousAttempt + 1, SubmittedAt = now, IsLate = now > assignment.DueAt
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
