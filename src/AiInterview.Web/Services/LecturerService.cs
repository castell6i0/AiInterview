using System.Security.Cryptography;
using System.Text;
using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AiInterview.Web.Services;

public sealed class LecturerService(AppDbContext db)
{
    public Task<List<Classroom>> ListAsync(string lecturerId) => db.Classes.AsNoTracking().Include(c => c.Members)
        .Where(c => c.LecturerId == lecturerId).OrderByDescending(c => c.CreatedAt).ToListAsync();

    public async Task<int> CreateAsync(string lecturerId, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150) throw new InvalidOperationException("Tên lớp phải dài từ 1 đến 150 ký tự.");
        string code;
        do { code = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)); }
        while (await db.Classes.AnyAsync(c => c.JoinCode == code));
        var classroom = new Classroom { LecturerId = lecturerId, Name = name.Trim(), JoinCode = code };
        db.Classes.Add(classroom);
        await db.SaveChangesAsync();
        return classroom.Id;
    }

    public async Task SetActiveAsync(int classId, string lecturerId, bool active)
    {
        var classroom = await db.Classes.SingleOrDefaultAsync(c => c.Id == classId && c.LecturerId == lecturerId)
            ?? throw new InvalidOperationException("Không tìm thấy lớp bạn quản lý.");
        classroom.IsActive = active;
        await db.SaveChangesAsync();
    }

    public async Task<LecturerClassVm?> DetailAsync(int classId, string lecturerId)
    {
        var classroom = await db.Classes.AsNoTracking().SingleOrDefaultAsync(c => c.Id == classId && c.LecturerId == lecturerId);
        if (classroom is null) return null;
        var members = await (from m in db.ClassMembers.AsNoTracking()
                             join u in db.Users.AsNoTracking() on m.StudentId equals u.Id
                             where m.ClassId == classId
                             select new StudentSummaryVm { StudentId = u.Id, Name = u.FullName, Email = u.Email ?? "", JoinedAt = m.JoinedAt })
            .OrderBy(m => m.Name).ToListAsync();
        var ids = members.Select(m => m.StudentId).ToArray();
        var sessions = await db.Sessions.AsNoTracking().Include(s => s.Evaluation).Where(s => ids.Contains(s.StudentId)).ToListAsync();
        foreach (var member in members) PopulateSummary(member, sessions.Where(s => s.StudentId == member.StudentId).ToList());
        var completed = sessions.Where(s => s.Status == SessionStatus.Completed && s.Evaluation is { IsProvisional: false }).ToList();
        var skillTurns = await db.Turns.AsNoTracking().Include(t => t.Evaluation).Where(t => ids.Contains(t.Session.StudentId)
            && t.Session.Status == SessionStatus.Completed && t.Session.Evaluation != null && !t.Session.Evaluation.IsProvisional
            && t.FollowUpDepth == 0 && t.Evaluation != null).ToListAsync();
        var skills = skillTurns.SelectMany(t => t.SkillTags.Split([',', ';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(tag => new { Tag = tag, Score = t.Evaluation!.OverallScore }))
            .GroupBy(x => x.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SkillAverageVm { Skill = g.Key, Score = g.Average(x => x.Score), SampleCount = g.Count() })
            .OrderBy(x => x.Score).Take(20).ToList();
        return new LecturerClassVm
        {
            Class = classroom, Students = members, Skills = skills, CompletedCount = completed.Count,
            AverageScore = completed.Count == 0 ? null : completed.Average(s => s.Evaluation!.OverallScore),
            Assignments = await db.Assignments.AsNoTracking().Include(a => a.Position).Where(a => a.ClassId == classId).OrderByDescending(a => a.CreatedAt).ToListAsync(),
            Submissions = await db.AssignmentSubmissions.AsNoTracking().Include(s => s.Session).ThenInclude(s => s.Evaluation)
                .Where(s => s.Assignment.ClassId == classId).OrderByDescending(s => s.SubmittedAt).ToListAsync()
        };
    }

    public async Task<LecturerStudentVm?> StudentAsync(int classId, string studentId, string lecturerId)
    {
        var classroom = await db.Classes.AsNoTracking().SingleOrDefaultAsync(c => c.Id == classId && c.LecturerId == lecturerId
            && c.Members.Any(m => m.StudentId == studentId));
        if (classroom is null) return null;
        var student = await (from m in db.ClassMembers.AsNoTracking()
                             join u in db.Users.AsNoTracking() on m.StudentId equals u.Id
                             where m.ClassId == classId && m.StudentId == studentId
                             select new StudentSummaryVm { StudentId = u.Id, Name = u.FullName, Email = u.Email ?? "", JoinedAt = m.JoinedAt }).SingleAsync();
        var sessions = await db.Sessions.AsNoTracking().Include(s => s.Position).Include(s => s.Evaluation)
            .Where(s => s.StudentId == studentId).OrderByDescending(s => s.CreatedAt).ToListAsync();
        PopulateSummary(student, sessions);
        var sessionIds = sessions.Select(s => s.Id).ToArray();
        return new LecturerStudentVm
        {
            Class = classroom, Student = student, Sessions = sessions,
            Comments = await db.LecturerComments.AsNoTracking().Where(c => c.ClassId == classId && c.LecturerId == lecturerId && sessionIds.Contains(c.SessionId))
                .OrderByDescending(c => c.CreatedAt).ToListAsync()
        };
    }

    private static void PopulateSummary(StudentSummaryVm summary, List<InterviewSession> sessions)
    {
        var completed = sessions.Where(s => s.Status == SessionStatus.Completed && s.Evaluation is { IsProvisional: false })
            .OrderByDescending(s => s.EndedAt ?? s.CreatedAt).ToList();
        summary.CompletedCount = completed.Count;
        summary.AverageScore = completed.Count == 0 ? null : completed.Average(s => s.Evaluation!.OverallScore);
        summary.LastPracticeAt = sessions.Count == 0 ? null : sessions.Max(s => s.EndedAt ?? s.StartedAt ?? s.CreatedAt);
        summary.Inactive = (summary.LastPracticeAt ?? summary.JoinedAt) < DateTimeOffset.UtcNow.AddDays(-14);
        summary.RepeatedLowScores = completed.Count >= 3 && completed.Take(3).All(s => s.Evaluation!.OverallScore < 5);
    }

    public async Task<AssignmentEditVm?> NewAssignmentAsync(int classId, string lecturerId) =>
        !await db.Classes.AnyAsync(c => c.Id == classId && c.LecturerId == lecturerId && c.IsActive) ? null : new AssignmentEditVm
        {
            ClassId = classId, Positions = await db.Positions.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync()
        };

    public async Task<int> CreateAssignmentAsync(AssignmentEditVm model, string lecturerId)
    {
        if (!await db.Classes.AnyAsync(c => c.Id == model.ClassId && c.LecturerId == lecturerId && c.IsActive))
            throw new InvalidOperationException("Không tìm thấy lớp đang mở do bạn quản lý.");
        if (!await db.Positions.AnyAsync(p => p.Id == model.PositionId && p.IsActive)) throw new InvalidOperationException("Vị trí không khả dụng.");
        var rubricId = await db.RubricVersions.Where(r => r.IsActive).Select(r => (int?)r.Id).SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("Chưa có rubric đang hoạt động. Vui lòng liên hệ quản trị viên.");
        var due = new DateTimeOffset(DateTime.SpecifyKind(model.DueAtVietnam, DateTimeKind.Unspecified), TimeSpan.FromHours(7));
        if (due <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Hạn nộp phải nằm trong tương lai (giờ Việt Nam).");
        var assignment = new Assignment
        {
            ClassId = model.ClassId, Title = model.Title.Trim(), Instructions = model.Instructions.Trim(), DueAt = due,
            PositionId = model.PositionId, Level = model.Level, Language = model.Language, Kind = model.Kind,
            QuestionCount = model.QuestionCount, RubricVersionId = rubricId
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();
        return assignment.Id;
    }

    public async Task<string> AddCommentAsync(LecturerCommentVm model, string lecturerId)
    {
        var studentId = await db.Sessions.Where(s => s.Id == model.SessionId
            && db.ClassMembers.Any(m => m.StudentId == s.StudentId && m.ClassId == model.ClassId && m.Class.LecturerId == lecturerId))
            .Select(s => s.StudentId).SingleOrDefaultAsync() ?? throw new InvalidOperationException("Buổi không thuộc sinh viên trong lớp bạn quản lý.");
        db.LecturerComments.Add(new LecturerComment { ClassId = model.ClassId, LecturerId = lecturerId, SessionId = model.SessionId, Content = model.Content.Trim() });
        await db.SaveChangesAsync();
        return studentId;
    }

    public async Task<byte[]?> ExportCsvAsync(int classId, string lecturerId)
    {
        var model = await DetailAsync(classId, lecturerId);
        if (model is null) return null;
        var csv = new StringBuilder();
        csv.AppendLine("\"Sinh viên\",\"Email\",\"Số buổi hoàn thành\",\"Điểm trung bình /10\",\"Luyện gần nhất (UTC+7)\",\"Cảnh báo\"");
        foreach (var row in model.Students)
        {
            var alert = string.Join("; ", new[] { row.Inactive ? "Không luyện ít nhất 14 ngày" : null, row.RepeatedLowScores ? "3 buổi gần nhất dưới 5" : null }.Where(x => x is not null));
            csv.AppendLine(string.Join(",", new[] { row.Name, row.Email, row.CompletedCount.ToString(), row.AverageScore?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "", row.LastPracticeAt?.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd HH:mm") ?? "", alert }.Select(CsvCell)));
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    public static string CsvCell(string value)
    {
        // A spreadsheet must never execute a user-controlled cell as a formula.
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) || value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
