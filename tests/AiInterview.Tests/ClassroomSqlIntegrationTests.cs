using System.Text;
using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.Identity;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AiInterview.Tests;

/// <summary>Exercises real SQL queries and transactions against freshly migrated test databases.</summary>
public sealed class ClassroomSqlIntegrationTests
{
    [SqlFact]
    public async Task MigratedAssignmentsAcceptCompletedSessionsAndTrackDistinctLateAttempts()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var lecturer = await CreateLecturer(sql);
        var student = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("AddSkillGapSnapshot"));
        var (classroom, assignment) = await CreateClassAndAssignment(db, lecturer.Id, student.Id);
        var secondAssignmentId = await new LecturerService(db).CreateAssignmentAsync(AssignmentInput(classroom.Id, assignment.PositionId), lecturer.Id);
        var session = Completed(student.Id, assignment);
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        var classes = new ClassroomService(db);
        var eligible = (await classes.AssignmentAsync(assignment.Id, student.Id))!;
        Assert.Equal(session.Id, Assert.Single(eligible.EligibleSessions).Id);
        await classes.SubmitAsync(assignment.Id, session.Id, student.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => classes.SubmitAsync(assignment.Id, session.Id, student.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => classes.SubmitAsync(secondAssignmentId, session.Id, student.Id));
        Assert.Empty((await classes.AssignmentAsync(assignment.Id, student.Id))!.EligibleSessions);
        Assert.Equal(session.Id, Assert.Single((await classes.AssignmentAsync(assignment.Id, student.Id))!.Submissions).SessionId);

        var next = Completed(student.Id, assignment);
        db.Sessions.Add(next);
        assignment.DueAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        await classes.SubmitAsync(assignment.Id, next.Id, student.Id);
        await using var verify = sql.Context();
        var attempts = await verify.AssignmentSubmissions.OrderBy(x => x.AttemptNumber).ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Equal(1, attempts[0].AttemptNumber);
        Assert.False(attempts[0].IsLate);
        Assert.Equal(2, attempts[1].AttemptNumber);
        Assert.True(attempts[1].IsLate);
        Assert.Equal(next.Id, attempts[1].SessionId);
        Assert.All(attempts, x => { Assert.Equal(student.Id, x.StudentId); Assert.Equal(assignment.Id, x.AssignmentId); });
        var detail = (await new ClassroomService(verify).DetailAsync(classroom.Id, student.Id))!;
        Assert.Equal(2, detail.Submissions.Count);
    }

    [SqlFact]
    public async Task SubmissionRejectsWrongOwnerConfigurationRubricTimeAndIncompleteSessions()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var lecturer = await CreateLecturer(sql);
        var student = await sql.CreateStudentAsync();
        var outsider = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var (_, assignment) = await CreateClassAndAssignment(db, lecturer.Id, student.Id);
        var differentPosition = await db.Positions.Where(p => p.Id != assignment.PositionId).Select(p => p.Id).FirstAsync();
        var otherRubric = new RubricVersion { Name = "Different assignment rubric", IsActive = false };
        db.RubricVersions.Add(otherRubric);
        await db.SaveChangesAsync();
        var cases = new (string Name, Action<InterviewSession> Change)[]
        {
            ("another student's session", s => s.StudentId = outsider.Id),
            ("position", s => s.PositionId = differentPosition),
            ("level", s => s.Level = InterviewLevel.Fresher),
            ("language", s => s.Language = InterviewLanguage.English),
            ("kind", s => s.Kind = InterviewKind.Behavioral),
            ("question count", s => { s.PlannedQuestionCount = 6; s.Turns.Add(AnsweredTurn(s.Id, 6)); }),
            ("rubric", s => s.RubricVersionId = otherRubric.Id),
            ("created before assignment", s => s.CreatedAt = assignment.CreatedAt.AddSeconds(-1)),
            ("unfinished", s => s.Status = SessionStatus.ReadyForEvaluation),
            ("ended early", s => s.EndedEarly = true),
            ("provisional evaluation", s => s.Evaluation!.IsProvisional = true),
            ("unanswered main question", s => s.Turns[4].Status = TurnStatus.Skipped),
            ("missing evaluation", s => s.Evaluation = null)
        };
        var rejected = new List<(string Name, Guid Id)>();
        foreach (var item in cases)
        {
            var session = Completed(student.Id, assignment);
            item.Change(session);
            db.Sessions.Add(session);
            rejected.Add((item.Name, session.Id));
        }
        var valid = Completed(student.Id, assignment);
        db.Sessions.Add(valid);
        await db.SaveChangesAsync();
        var classes = new ClassroomService(db);
        Assert.Equal(valid.Id, Assert.Single((await classes.AssignmentAsync(assignment.Id, student.Id))!.EligibleSessions).Id);
        foreach (var item in rejected)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => classes.SubmitAsync(assignment.Id, item.Id, student.Id));
            Assert.Contains("điều kiện", error.Message);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => classes.SubmitAsync(assignment.Id, valid.Id, outsider.Id));
        Assert.Null(await classes.AssignmentAsync(assignment.Id, outsider.Id));
        Assert.False(await db.AssignmentSubmissions.AnyAsync());
        await classes.SubmitAsync(assignment.Id, valid.Id, student.Id);
        Assert.Equal(valid.Id, (await db.AssignmentSubmissions.SingleAsync()).SessionId);
    }

    [SqlFact]
    public async Task JoinRequiresSharingConsentIsIdempotentAndClosedClassesRejectNewWork()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var lecturer = await CreateLecturer(sql);
        var student = await sql.CreateStudentAsync();
        var outsider = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var lecturers = new LecturerService(db);
        var classId = await lecturers.CreateAsync(lecturer.Id, "  Lớp kiểm thử  ");
        var classroom = await db.Classes.SingleAsync(c => c.Id == classId);
        Assert.Equal("Lớp kiểm thử", classroom.Name);
        var classes = new ClassroomService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => classes.JoinAsync(student.Id,
            new JoinClassVm { Code = classroom.JoinCode, AcceptSharing = false }));
        Assert.False(await db.ClassMembers.AnyAsync());
        var join = new JoinClassVm { Code = "  " + classroom.JoinCode.ToLowerInvariant() + "  ", AcceptSharing = true };
        Assert.Equal(classId, await classes.JoinAsync(student.Id, join));
        Assert.Equal(classId, await classes.JoinAsync(student.Id, join));
        Assert.Equal(1, await db.ClassMembers.CountAsync());
        Assert.Equal(classId, Assert.Single((await classes.ListAsync(student.Id)).Classes).Id);
        Assert.Empty((await classes.ListAsync(outsider.Id)).Classes);
        Assert.Null(await classes.DetailAsync(classId, outsider.Id));

        var position = await db.Positions.Select(p => p.Id).FirstAsync();
        var assignmentId = await lecturers.CreateAssignmentAsync(AssignmentInput(classId, position), lecturer.Id);
        var assignment = await db.Assignments.SingleAsync(a => a.Id == assignmentId);
        var session = Completed(student.Id, assignment);
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        await lecturers.SetActiveAsync(classId, lecturer.Id, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => classes.JoinAsync(outsider.Id, new JoinClassVm { Code = classroom.JoinCode, AcceptSharing = true }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => classes.SubmitAsync(assignmentId, session.Id, student.Id));
        Assert.Null(await lecturers.NewAssignmentAsync(classId, lecturer.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lecturers.CreateAssignmentAsync(AssignmentInput(classId, position), lecturer.Id));
        Assert.NotNull(await classes.DetailAsync(classId, student.Id));
        Assert.Equal(1, await db.ClassMembers.CountAsync());
        Assert.False(await db.AssignmentSubmissions.AnyAsync());
    }

    [SqlFact]
    public async Task AssignmentsUseVietnamTimeAndPreserveTheirOriginalRubric()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var lecturer = await CreateLecturer(sql);
        var otherLecturer = await CreateLecturer(sql);
        var student = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var lecturers = new LecturerService(db);
        var (classroom, original) = await CreateClassAndAssignment(db, lecturer.Id, student.Id);
        var input = AssignmentInput(classroom.Id, original.PositionId);
        input.DueAtVietnam = new DateTime(2031, 2, 3, 15, 45, 0, DateTimeKind.Unspecified);
        Assert.Null(await lecturers.NewAssignmentAsync(classroom.Id, otherLecturer.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lecturers.CreateAssignmentAsync(input, otherLecturer.Id));
        input.DueAtVietnam = DateTime.UtcNow.AddHours(7).AddMinutes(-1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lecturers.CreateAssignmentAsync(input, lecturer.Id));
        input.DueAtVietnam = new DateTime(2031, 2, 3, 15, 45, 0, DateTimeKind.Unspecified);
        var assignmentId = await lecturers.CreateAssignmentAsync(input, lecturer.Id);
        var saved = await db.Assignments.SingleAsync(a => a.Id == assignmentId);
        Assert.Equal(new DateTimeOffset(2031, 2, 3, 8, 45, 0, TimeSpan.Zero), saved.DueAt.ToUniversalTime());
        var oldRubric = await db.RubricVersions.SingleAsync(r => r.IsActive);
        oldRubric.IsActive = false;
        await db.SaveChangesAsync();
        var newRubric = new RubricVersion { Name = "New active rubric for later work", IsActive = true };
        db.RubricVersions.Add(newRubric);
        await db.SaveChangesAsync();
        await using var verify = sql.Context();
        Assert.Equal(oldRubric.Id, (await verify.Assignments.SingleAsync(a => a.Id == assignmentId)).RubricVersionId);
        var followingId = await new LecturerService(verify).CreateAssignmentAsync(input, lecturer.Id);
        Assert.Equal(newRubric.Id, (await verify.Assignments.SingleAsync(a => a.Id == followingId)).RubricVersionId);
    }

    [SqlFact]
    public async Task LecturerViewsCommentsAndCsvAreRestrictedToOwnedClassesAndMembers()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var lecturer = await CreateLecturer(sql);
        var otherLecturer = await CreateLecturer(sql);
        var student = await sql.CreateStudentAsync();
        var outsider = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        (await db.Users.SingleAsync(u => u.Id == student.Id)).FullName = "=1+1";
        (await db.Users.SingleAsync(u => u.Id == outsider.Id)).FullName = "Private outsider";
        var (classroom, assignment) = await CreateClassAndAssignment(db, lecturer.Id, student.Id);
        var (otherClass, _) = await CreateClassAndAssignment(db, otherLecturer.Id, student.Id);
        var completed = Completed(student.Id, assignment);
        var provisional = Completed(student.Id, assignment);
        provisional.Evaluation!.IsProvisional = true;
        provisional.Evaluation.OverallScore = 1;
        var privateSession = Completed(outsider.Id, assignment);
        db.Sessions.AddRange(completed, provisional, privateSession);
        await db.SaveChangesAsync();
        var lecturers = new LecturerService(db);
        Assert.Equal(classroom.Id, Assert.Single(await lecturers.ListAsync(lecturer.Id)).Id);
        Assert.Null(await lecturers.DetailAsync(classroom.Id, otherLecturer.Id));
        Assert.Null(await lecturers.StudentAsync(classroom.Id, student.Id, otherLecturer.Id));
        Assert.Null(await lecturers.StudentAsync(classroom.Id, outsider.Id, lecturer.Id));
        Assert.Null(await lecturers.ExportCsvAsync(classroom.Id, otherLecturer.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lecturers.SetActiveAsync(classroom.Id, otherLecturer.Id, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lecturers.AddCommentAsync(new LecturerCommentVm
        {
            ClassId = classroom.Id, SessionId = completed.Id, Content = "Unauthorized lecturer"
        }, otherLecturer.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lecturers.AddCommentAsync(new LecturerCommentVm
        {
            ClassId = classroom.Id, SessionId = privateSession.Id, Content = "Nonmember session"
        }, lecturer.Id));
        Assert.Equal(student.Id, await lecturers.AddCommentAsync(new LecturerCommentVm
        {
            ClassId = classroom.Id, SessionId = completed.Id, Content = "  Giải thích tốt; bổ sung ví dụ.  "
        }, lecturer.Id));
        await lecturers.AddCommentAsync(new LecturerCommentVm
        {
            ClassId = otherClass.Id, SessionId = completed.Id, Content = "Other class comment"
        }, otherLecturer.Id);
        var studentDetail = (await lecturers.StudentAsync(classroom.Id, student.Id, lecturer.Id))!;
        Assert.Equal("Giải thích tốt; bổ sung ví dụ.", Assert.Single(studentDetail.Comments).Content);
        Assert.Equal(2, studentDetail.Sessions.Count);
        Assert.DoesNotContain(studentDetail.Sessions, s => s.Id == privateSession.Id);
        var detail = (await lecturers.DetailAsync(classroom.Id, lecturer.Id))!;
        Assert.Equal(student.Id, Assert.Single(detail.Students).StudentId);
        Assert.Equal(1, detail.CompletedCount);
        Assert.Equal(8m, detail.AverageScore);
        Assert.Equal(1, detail.Students[0].CompletedCount);
        var skill = Assert.Single(detail.Skills);
        Assert.Equal("SQL", skill.Skill);
        Assert.Equal(8m, skill.Score);
        Assert.Equal(5, skill.SampleCount);
        var csv = (await lecturers.ExportCsvAsync(classroom.Id, lecturer.Id))!;
        Assert.True(csv.Take(3).SequenceEqual(Encoding.UTF8.GetPreamble()));
        var text = Encoding.UTF8.GetString(csv);
        Assert.Contains("\"'=1+1\"", text);
        Assert.Contains(student.Email!, text);
        Assert.Contains("\"1\",\"8.00\"", text);
        Assert.DoesNotContain(outsider.Email!, text);
        Assert.DoesNotContain("Private outsider", text);
        await using var verify = sql.Context();
        Assert.Equal(2, await verify.LecturerComments.CountAsync());
        Assert.True((await verify.Classes.SingleAsync(c => c.Id == classroom.Id)).IsActive);
    }

    private static async Task<ApplicationUser> CreateLecturer(SqlIntegrationTests.SqlDatabase sql)
    {
        var user = await sql.CreateStudentAsync();
        await using var scope = sql.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var current = (await users.FindByIdAsync(user.Id))!;
        Assert.True((await users.RemoveFromRoleAsync(current, Roles.Student)).Succeeded);
        Assert.True((await users.AddToRoleAsync(current, Roles.Lecturer)).Succeeded);
        return current;
    }

    private static async Task<(Classroom Class, Assignment Assignment)> CreateClassAndAssignment(AppDbContext db, string lecturer, string student)
    {
        var service = new LecturerService(db);
        var classId = await service.CreateAsync(lecturer, "Lớp tích hợp SQL");
        var classroom = await db.Classes.SingleAsync(c => c.Id == classId);
        await new ClassroomService(db).JoinAsync(student, new JoinClassVm { Code = classroom.JoinCode, AcceptSharing = true });
        var position = await db.Positions.Where(p => p.IsActive).Select(p => p.Id).FirstAsync();
        var assignmentId = await service.CreateAssignmentAsync(AssignmentInput(classId, position), lecturer);
        return (classroom, await db.Assignments.SingleAsync(a => a.Id == assignmentId));
    }

    private static AssignmentEditVm AssignmentInput(int classId, int positionId) => new()
    {
        ClassId = classId, Title = "Bài luyện đầy đủ", Instructions = "Hoàn thành năm câu và nộp báo cáo.",
        PositionId = positionId, QuestionCount = 5, Level = InterviewLevel.Intern,
        Language = InterviewLanguage.Vietnamese, Kind = InterviewKind.Technical,
        DueAtVietnam = DateTime.UtcNow.AddDays(1).AddHours(7)
    };

    private static InterviewSession Completed(string student, Assignment assignment)
    {
        var session = new InterviewSession
        {
            StudentId = student, PositionId = assignment.PositionId, RubricVersionId = assignment.RubricVersionId,
            Level = assignment.Level, Language = assignment.Language, Kind = assignment.Kind,
            PlannedQuestionCount = assignment.QuestionCount, CreatedAt = DateTimeOffset.UtcNow,
            EndedAt = DateTimeOffset.UtcNow, Status = SessionStatus.Completed
        };
        session.Evaluation = new Evaluation
        {
            SessionId = session.Id, OverallScore = 8, ReadinessScore = 80, CompletionRate = 100,
            Verdict = "Hire", IsProvisional = false, Model = "persisted-test-fixture"
        };
        session.Turns = Enumerable.Range(1, assignment.QuestionCount).Select(n => AnsweredTurn(session.Id, n)).ToList();
        return session;
    }

    private static InterviewTurn AnsweredTurn(Guid sessionId, int n)
    {
        var turn = new InterviewTurn
        {
            SessionId = sessionId, SequenceNumber = n, MainQuestionNumber = n, FollowUpDepth = 0,
            Question = $"SQL question {n}", Answer = "A supported answer.", Status = TurnStatus.Answered, SkillTags = "SQL",
            SubmittedAt = DateTimeOffset.UtcNow
        };
        turn.Evaluation = new TurnEvaluation { TurnId = turn.Id, OverallScore = 8 };
        return turn;
    }
}
