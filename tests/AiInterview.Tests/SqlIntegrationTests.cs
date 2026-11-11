using System.Security.Claims;
using System.Text.Json;
using AiInterview.Application.DTOs;
using AiInterview.Application.Interfaces;
using AiInterview.Application.Services;
using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.AI;
using AiInterview.Infrastructure.Documents;
using AiInterview.Infrastructure.Identity;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.Controllers;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiInterview.Tests;

/// <summary>Opt in with AIINTERVIEW_TEST_SQL=1. Requires the AiInterviewDev LocalDB instance.</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AIINTERVIEW_TEST_SQL") != "1")
            Skip = "Set AIINTERVIEW_TEST_SQL=1 to run isolated SQL Server integration tests.";
    }
}

/// <summary>
/// Real SQL Server, Identity and application services. The scripted AI below exists only in
/// this test file: it verifies persistence/recovery, and does not verify live Gemini responses.
/// </summary>
public sealed class SqlIntegrationTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [SqlFact]
    public async Task StaleOrMissingProviderConsentBlocksCvGapAndNewSessionUntilConfirmedAgain()
    {
        await using var sql = await SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var (cv, jd) = await AddDocuments(db, owner.Id);
        var ai = new ScriptedAi { Provider = "OpenAI" };
        var interviews = Service(db, ai);
        await using var scope = sql.Services.CreateAsyncScope();
        var profiles = new ProfileService(db, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), new CvTextExtractor(), ai);
        var input = await Input(db);
        input.CvProfileId = cv.Id;
        input.JobDescriptionId = jd.Id;
        foreach (var provider in new[] { "Gemini", "", "unknown" })
        {
            input.ConsentedProvider = provider;
            await Assert.ThrowsAsync<InvalidOperationException>(() => interviews.CreateAsync(owner.Id, input, Ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => profiles.AnalyzeCvAsync(owner.Id, cv.Id, true, provider, Ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => profiles.CompareGapAsync(owner.Id,
                new CompareGapModel { CvId = cv.Id, JdId = jd.Id, Consent = true, ConsentedProvider = provider }, Ct));
        }
        Assert.Empty(ai.Requests);
        Assert.False(await db.Sessions.AnyAsync());
        Assert.Null(cv.AnalysisJson);
        Assert.Null(jd.SkillGapJson);

        // Re-rendering the stale form keeps the user's choices but clears the checked consent.
        input.ConsentedProvider = "Gemini";
        var controller = new InterviewsController(interviews, db, ai) { ControllerContext = ControllerFor(owner.Id) };
        controller.ModelState.SetModelValue(nameof(input.ConsentToAi), "true", "true");
        controller.ModelState.SetModelValue(nameof(input.ConsentedProvider), "Gemini", "Gemini");
        var failed = Assert.IsType<ViewResult>(await controller.Create(input, Ct));
        Assert.Same(input, failed.Model);
        Assert.False(input.ConsentToAi);
        Assert.False(controller.ModelState.ContainsKey(nameof(input.ConsentToAi)));
        Assert.False(controller.ModelState.ContainsKey(nameof(input.ConsentedProvider)));
        Assert.Equal(cv.Id, input.CvProfileId);
        Assert.Empty(ai.Requests);

        input.ConsentedProvider = "OpenAI";
        input.ConsentToAi = true;
        var id = await interviews.CreateAsync(owner.Id, input, Ct);
        ai.Enqueue(new CvAnalysis { Summary = "Has C# project experience.", Skills = ["C#"], Projects = [], Experience = [] });
        ai.Enqueue(new SkillGapAnalysis { MatchedSkills = ["C#"], MissingEvidence = ["Testing"], Recommendations = ["Practice testing scenarios."] });
        await profiles.AnalyzeCvAsync(owner.Id, cv.Id, true, "OpenAI", Ct);
        await profiles.CompareGapAsync(owner.Id, new CompareGapModel
        { CvId = cv.Id, JdId = jd.Id, Consent = true, ConsentedProvider = "OpenAI" }, Ct);
        Assert.Equal(new[] { "cv-analysis", "skill-gap" }, ai.Requests.Select(r => r.Operation));
        await using var verify = sql.Context();
        Assert.Equal("OpenAI", (await verify.Sessions.SingleAsync(s => s.Id == id)).AiProvider);
        Assert.NotNull((await verify.CvProfiles.SingleAsync()).AnalysisJson);
        Assert.NotNull((await verify.JobDescriptions.SingleAsync()).SkillGapJson);
    }

    [SqlFact]
    public async Task DashboardPracticeCarriesDisplayedProviderAndBlocksStaleConsent()
    {
        await using var sql = await SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var ai = new ScriptedAi();
        var interviews = Service(db, ai);
        var original = await Input(db);
        var id = await interviews.CreateAsync(owner.Id, original, Ct);
        var session = await db.Sessions.SingleAsync(s => s.Id == id);
        session.Status = SessionStatus.Completed;
        db.Evaluations.Add(new Evaluation { SessionId = id, CompletionRate = 100, IsProvisional = false, OverallScore = 4 });
        // Explicit Add marks the preassigned GUID turn as new rather than an existing related row.
        db.Turns.Add(new InterviewTurn
        {
            SessionId = id, SequenceNumber = 1, MainQuestionNumber = 1, Question = "Explain testing.", Answer = "I check boundary values.",
            Status = TurnStatus.Answered, SkillTags = "Testing", Evaluation = new TurnEvaluation { OverallScore = 4 }
        });
        await db.SaveChangesAsync();
        var context = ControllerFor(owner.Id);
        var controller = new DashboardController(new DashboardService(db), interviews)
        { ControllerContext = context, TempData = new TempDataDictionary(context.HttpContext, new ConsentTempDataProvider()) };
        var input = new PracticeWeakModel
        { PositionId = original.PositionId, Language = original.Language, Level = original.Level, Consent = true, ConsentedProvider = "OpenAI" };
        var rejected = Assert.IsType<RedirectToActionResult>(await controller.PracticeWeak(input, Ct));
        Assert.Equal(nameof(DashboardController.Index), rejected.ActionName);
        Assert.NotNull(controller.TempData["DashboardError"]);
        Assert.Equal(1, await db.Sessions.CountAsync());
        input.ConsentedProvider = "Gemini";
        var accepted = Assert.IsType<RedirectToActionResult>(await controller.PracticeWeak(input, Ct));
        Assert.Equal(nameof(InterviewsController.Room), accepted.ActionName);
        Assert.Equal("Interviews", accepted.ControllerName);
        var created = await db.Sessions.SingleAsync(s => s.Id != id);
        Assert.Equal("Gemini", created.AiProvider);
        Assert.Equal("Testing", created.FocusSkills);
        Assert.Empty(ai.Requests);
    }

    private sealed class ConsentTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    [SqlFact]
    public async Task SeedIsIdempotentAndDoesNotPromoteExistingIdentityAccounts()
    {
        await using var sql = await SqlDatabase.CreateAsync();
        var existing = await sql.CreateStudentAsync("admin@example.test");
        await using (var db = sql.Context())
        {
            (await db.Positions.OrderBy(x => x.Id).FirstAsync()).Description = "Administrator's edited description";
            (await db.Questions.OrderBy(x => x.Id).FirstAsync()).CompanyType = "Edited";
            await db.SaveChangesAsync();
        }

        var seed = SeedConfiguration(withAccounts: true);
        await DatabaseSeeder.SeedAsync(sql.Services, seed, development: true);
        await DatabaseSeeder.SeedAsync(sql.Services, seed, development: true);
        await using var scope = sql.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var persisted = (await users.FindByIdAsync(existing.Id))!;
        Assert.True(await users.IsInRoleAsync(persisted, Roles.Student));
        Assert.False(await users.IsInRoleAsync(persisted, Roles.Admin));
        Assert.True(await users.CheckPasswordAsync(persisted, SqlDatabase.Password));
        var lecturer = (await users.FindByEmailAsync("lecturer@example.test"))!;
        Assert.True(await users.IsInRoleAsync(lecturer, Roles.Lecturer));
        var student = (await users.FindByEmailAsync("student@example.test"))!;
        Assert.True(await users.IsInRoleAsync(student, Roles.Student));

        await using var verify = sql.Context();
        Assert.Equal(3, await verify.Roles.CountAsync());
        Assert.Equal(3, await verify.Users.CountAsync());
        Assert.Equal(63, await verify.Questions.CountAsync());
        Assert.Equal(7, await verify.Positions.CountAsync());
        Assert.Equal(1, await verify.RubricVersions.CountAsync(x => x.IsActive));
        Assert.Equal(5, await verify.RubricCriteria.CountAsync());
        Assert.Equal(100m, await verify.RubricCriteria.SumAsync(x => x.Weight));
        Assert.Equal(1, await verify.StudentProfiles.CountAsync(x => x.UserId == student.Id));
        Assert.Equal("Administrator's edited description", (await verify.Positions.OrderBy(x => x.Id).FirstAsync()).Description);
        Assert.Equal("Edited", (await verify.Questions.OrderBy(x => x.Id).FirstAsync()).CompanyType);
        Assert.Equal(await verify.QuestionSkillTags.CountAsync(),
            await verify.QuestionSkillTags.Select(x => new { x.QuestionBankId, x.SkillTagId }).Distinct().CountAsync());

        var duplicateEmails = SeedConfiguration(withAccounts: true, adminEmail: "student@example.test");
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseSeeder.SeedAsync(sql.Services, duplicateEmails, true));
        Assert.False(await users.IsInRoleAsync(student, Roles.Admin));
    }

    [SqlFact]
    public async Task CvJdAndSessionOperationsAreScopedToTheirOwner()
    {
        await using var sql = await SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        var other = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var (cv, jd) = await AddDocuments(db, owner.Id);
        var ai = new ScriptedAi();
        var interviews = Service(db, ai);
        var input = await Input(db);
        input.CvProfileId = cv.Id;
        input.JobDescriptionId = jd.Id;
        var id = await interviews.CreateAsync(owner.Id, input, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => interviews.CreateAsync(other.Id, input, Ct));
        input.CvProfileId = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => interviews.CreateAsync(other.Id, input, Ct));
        var options = new CreateInterviewModel();
        await interviews.PopulateAsync(options, other.Id, Ct);
        Assert.Empty(options.Cvs);
        Assert.Empty(options.Jds);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => interviews.GetOwnedAsync(other.Id, id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => interviews.GenerateNextAsync(other.Id, id, null, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => interviews.EndAsync(other.Id, id, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => interviews.EvaluateAsync(other.Id, id, null, Ct));

        await using var scope = sql.Services.CreateAsyncScope();
        var profile = new ProfileService(db, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), new CvTextExtractor(), ai);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => profile.CvAsync(other.Id, cv.Id, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => profile.JdAsync(other.Id, jd.Id, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => profile.DeleteCvAsync(other.Id, cv.Id, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => profile.DeleteJdAsync(other.Id, jd.Id, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => profile.AnalyzeCvAsync(other.Id, cv.Id, true, "Gemini", Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => profile.CompareGapAsync(other.Id,
            new CompareGapModel { JdId = jd.Id, CvId = cv.Id, Consent = true, ConsentedProvider = "Gemini" }, Ct));
        Assert.Empty(ai.Requests);

        ai.Enqueue(Question("Explain dependency injection."));
        var turn = (await interviews.GenerateNextAsync(owner.Id, id, null, Ct))!;
        await interviews.DraftAsync(other.Id, id, new DraftAnswerModel { TurnId = turn.Id, Answer = "Intruder draft" }, Ct);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => interviews.AnswerAsync(other.Id, id,
            new SubmitAnswerModel { TurnId = turn.Id, Answer = "Intruder answer" }, Ct));
        var controller = new InterviewsController(interviews, db, ai)
        {
            ControllerContext = ControllerFor(other.Id)
        };
        Assert.IsType<NotFoundResult>(await controller.Report(id, Ct));
        await controller.Mark(id, new MarkTurnModel { TurnId = turn.Id, Starred = true, NeedsPractice = true }, Ct);

        await using var verify = sql.Context();
        var saved = await verify.Turns.SingleAsync(x => x.Id == turn.Id);
        Assert.Null(saved.Answer);
        Assert.Null(saved.DraftAnswer);
        Assert.False(saved.Starred);
        Assert.False(saved.NeedsPractice);
        Assert.Equal(TurnStatus.Pending, saved.Status);
        Assert.Equal(1, await verify.Sessions.CountAsync());
        Assert.Equal(1, await verify.CvProfiles.CountAsync());
        Assert.Equal(1, await verify.JobDescriptions.CountAsync());
    }

    [SqlFact]
    public async Task CreateRequiresConsentAndPersistsImmutableCvAndJdSnapshots()
    {
        await using var sql = await SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var (cv, jd) = await AddDocuments(db, owner.Id);
        var ai = new ScriptedAi();
        var interviews = Service(db, ai);
        var input = await Input(db);
        input.CvProfileId = cv.Id;
        input.JobDescriptionId = jd.Id;
        input.ConsentToAi = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => interviews.CreateAsync(owner.Id, input, Ct));
        Assert.False(await db.Sessions.AnyAsync());
        input.ConsentToAi = true;
        input.FocusSkills = "  SQL and testing  ";
        input.Persona = InterviewPersona.Challenger;
        input.Mode = InterviewMode.Voice;
        input.Level = InterviewLevel.Fresher;
        input.Language = InterviewLanguage.English;
        input.Kind = InterviewKind.JobDescription;
        jd.ComparedCvId = cv.Id;
        jd.SkillGapJson = AiPrompts.Json(new SkillGapAnalysis { MissingEvidence = ["SQL transaction isolation"], Recommendations = ["Explain a transaction boundary"], MatchedSkills = ["C#"] });
        await db.SaveChangesAsync();
        var originalGap = jd.SkillGapJson;
        var id = await interviews.CreateAsync(owner.Id, input, Ct);
        var originalCv = cv.ExtractedText;
        var originalJd = jd.Content;
        cv.ExtractedText = "Changed after session creation";
        jd.Content = "Changed job description after session creation";
        jd.SkillGapJson = null;
        await db.SaveChangesAsync();

        await using var verify = sql.Context();
        var saved = await verify.Sessions.SingleAsync(x => x.Id == id);
        Assert.Equal(originalCv, saved.CvSnapshot);
        Assert.Equal(originalJd, saved.JdSnapshot);
        Assert.Equal(originalGap, saved.SkillGapSnapshotJson);
        Assert.Equal(cv.Id, saved.CvProfileId);
        Assert.Equal(jd.Id, saved.JobDescriptionId);
        Assert.Equal("SQL and testing", saved.FocusSkills);
        Assert.Equal(InterviewPersona.Challenger, saved.Persona);
        Assert.Equal(InterviewMode.Voice, saved.Mode);
        Assert.Equal(InterviewLevel.Fresher, saved.Level);
        Assert.Equal(InterviewLanguage.English, saved.Language);
        Assert.Equal(InterviewKind.JobDescription, saved.Kind);
        Assert.Equal(SessionStatus.Created, saved.Status);
        Assert.Equal(8, saved.RowVersion.Length);
        Assert.Empty(ai.Requests);
        ai.Enqueue(Question("Explain SQL transaction isolation."));
        await interviews.GenerateNextAsync(owner.Id, id, null, Ct);
        var sentGap = JsonSerializer.SerializeToElement(ai.Requests.Single().Data).GetProperty("skillGap");
        Assert.Contains("SQL transaction isolation", sentGap.ToString());
        input.CvProfileId = null;
        var unmatchedId = await interviews.CreateAsync(owner.Id, input, Ct);
        Assert.Null((await interviews.GetOwnedAsync(owner.Id, unmatchedId)).SkillGapSnapshotJson);
    }

    [SqlFact]
    public async Task DraftAndSubmittedAnswerSurviveNewContextsAndDuplicateSubmission()
    {
        await using var sql = await SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        var ai = new ScriptedAi();
        ai.Enqueue(Question("Explain SQL indexes."));
        Guid id;
        Guid turnId;
        await using (var db = sql.Context())
        {
            var service = Service(db, ai);
            id = await service.CreateAsync(owner.Id, await Input(db), Ct);
            turnId = (await service.GenerateNextAsync(owner.Id, id, null, Ct))!.Id;
            await service.DraftAsync(owner.Id, id, new DraftAnswerModel { TurnId = turnId, Answer = "Unsaved editing becomes a saved draft" }, Ct);
        }
        await using (var db = sql.Context())
        {
            var service = Service(db, ai);
            var existing = (await service.GenerateNextAsync(owner.Id, id, null, Ct))!;
            Assert.Equal(turnId, existing.Id);
            Assert.Equal("Unsaved editing becomes a saved draft", existing.DraftAnswer);
            Assert.Single(ai.Requests);
            await service.AnswerAsync(owner.Id, id, new SubmitAnswerModel
            {
                TurnId = turnId, Answer = "  Indexes speed up lookup operations.  ",
                Telemetry = new SpeechTelemetry { ElapsedMs = 10000, FirstSpeechMs = 2000 }
            }, Ct);
        }
        DateTimeOffset? submitted;
        await using (var db = sql.Context())
        {
            var saved = await db.Turns.SingleAsync(x => x.Id == turnId);
            submitted = saved.SubmittedAt;
            await Service(db, ai).AnswerAsync(owner.Id, id,
                new SubmitAnswerModel { TurnId = turnId, Answer = "Duplicate must not overwrite the first answer" }, Ct);
            await Service(db, ai).DraftAsync(owner.Id, id,
                new DraftAnswerModel { TurnId = turnId, Answer = "Late draft must not overwrite submission" }, Ct);
        }
        await using var verify = sql.Context();
        var persisted = await verify.Turns.Include(x => x.DeliveryMetric).SingleAsync(x => x.Id == turnId);
        Assert.Equal("Indexes speed up lookup operations.", persisted.Answer);
        Assert.Null(persisted.DraftAnswer);
        Assert.Equal(TurnStatus.Answered, persisted.Status);
        Assert.Equal(submitted, persisted.SubmittedAt);
        Assert.NotNull(persisted.QuestionDisplayedAt);
        Assert.NotNull(persisted.AnswerStartedAt);
        Assert.Equal(5, persisted.DeliveryMetric!.WordCount);
        Assert.Null(persisted.DeliveryMetric.WordsPerMinute);
        Assert.Null(persisted.SpeechEventsJson);
        Assert.Equal(1, await verify.DeliveryMetrics.CountAsync());
        var session = await verify.Sessions.SingleAsync(x => x.Id == id);
        Assert.Equal(SessionStatus.AwaitingQuestion, session.Status);
        Assert.Null(session.BusyUntil);
        Assert.Null(session.OperationId);
    }

    [SqlFact]
    public async Task FailedNextQuestionKeepsTheSavedAnswerAndCanBeRetried()
    {
        await using var sql = await SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        var ai = new ScriptedAi();
        ai.Enqueue(Question("Explain SQL transactions."));
        ai.EnqueueFailure();
        ai.Enqueue(Question("Describe transaction isolation."));
        Guid id;
        Guid turnId;
        await using (var db = sql.Context())
        {
            var service = Service(db, ai);
            id = await service.CreateAsync(owner.Id, await Input(db), Ct);
            turnId = (await service.GenerateNextAsync(owner.Id, id, null, Ct))!.Id;
            await service.AnswerAsync(owner.Id, id, new SubmitAnswerModel { TurnId = turnId, Answer = "Transactions are atomic." }, Ct);
            await Assert.ThrowsAsync<AiServiceException>(() => service.GenerateNextAsync(owner.Id, id, null, Ct));
        }
        await using (var verify = sql.Context())
        {
            var saved = await verify.Turns.SingleAsync(x => x.Id == turnId);
            Assert.Equal("Transactions are atomic.", saved.Answer);
            Assert.Equal(TurnStatus.Answered, saved.Status);
            Assert.Equal(1, await verify.Turns.CountAsync());
            var session = await verify.Sessions.SingleAsync(x => x.Id == id);
            Assert.Equal(SessionStatus.AwaitingQuestion, session.Status);
            Assert.Null(session.OperationId);
            Assert.Null(session.BusyUntil);
            var next = (await Service(verify, ai).GenerateNextAsync(owner.Id, id, null, Ct))!;
            Assert.Equal(2, next.SequenceNumber);
            Assert.Equal(2, next.MainQuestionNumber);
        }
        Assert.Equal(3, ai.Requests.Count);
    }

    [SqlFact]
    public async Task FollowUpsAreLinkedAndAThirdFollowUpIsRejectedWithoutPersistence()
    {
        await using var sql = await SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var ai = new ScriptedAi();
        ai.Enqueue(Question("Explain interfaces."));
        ai.Enqueue(Question("Give one concrete example.", followUp: true));
        ai.Enqueue(Question("Explain that example's tradeoff.", followUp: true));
        ai.Enqueue(Question("Forbidden third follow-up.", followUp: true));
        ai.Enqueue(Question("Explain testing."));
        var service = Service(db, ai);
        var id = await service.CreateAsync(owner.Id, await Input(db), Ct);
        var ids = new List<Guid>();
        for (var depth = 0; depth < 3; depth++)
        {
            var turn = (await service.GenerateNextAsync(owner.Id, id, null, Ct))!;
            ids.Add(turn.Id);
            Assert.Equal(depth, turn.FollowUpDepth);
            Assert.Equal(1, turn.MainQuestionNumber);
            await service.AnswerAsync(owner.Id, id, new SubmitAnswerModel { TurnId = turn.Id, Answer = "A concrete supported example." }, Ct);
        }
        await Assert.ThrowsAsync<AiServiceException>(() => service.GenerateNextAsync(owner.Id, id, null, Ct));
        await using (var verify = sql.Context())
        {
            var turns = await verify.Turns.OrderBy(t => t.SequenceNumber).ToListAsync();
            Assert.Equal(3, turns.Count);
            Assert.Null(turns[0].RootTurnId);
            Assert.Equal(ids[0], turns[1].RootTurnId);
            Assert.Equal(ids[0], turns[1].ParentTurnId);
            Assert.Equal(ids[0], turns[2].RootTurnId);
            Assert.Equal(ids[1], turns[2].ParentTurnId);
        }
        Assert.False(JsonSerializer.SerializeToElement(ai.Requests[3].Data).GetProperty("allowFollowUp").GetBoolean());
        var nextMain = (await service.GenerateNextAsync(owner.Id, id, null, Ct))!;
        Assert.Equal(0, nextMain.FollowUpDepth);
        Assert.Equal(2, nextMain.MainQuestionNumber);
        Assert.Equal(4, nextMain.SequenceNumber);
        Assert.Null(nextMain.RootTurnId);
        Assert.Null(nextMain.ParentTurnId);
    }

    [SqlFact]
    public async Task EvaluationResumesAfterPartialFailureWithoutRegradingSavedTurns()
    {
        await using var sql = await SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        var ai = new ScriptedAi();
        ai.Enqueue(Question("Explain atomicity."));
        ai.Enqueue(Question("Explain durability."));
        ai.Enqueue(EvaluateAnswer);
        ai.EnqueueFailure();
        ai.Enqueue(EvaluateAnswer);
        ai.Enqueue(Summarize);
        Guid id;
        Guid firstTurn;
        await using (var db = sql.Context())
        {
            var service = Service(db, ai);
            id = await service.CreateAsync(owner.Id, await Input(db), Ct);
            var first = (await service.GenerateNextAsync(owner.Id, id, null, Ct))!;
            firstTurn = first.Id;
            await service.AnswerAsync(owner.Id, id, new SubmitAnswerModel { TurnId = first.Id, Answer = "Atomicity applies all operations or none." }, Ct);
            var second = (await service.GenerateNextAsync(owner.Id, id, null, Ct))!;
            await service.AnswerAsync(owner.Id, id, new SubmitAnswerModel { TurnId = second.Id, Answer = "Durability preserves committed data." }, Ct);
            await service.EndAsync(owner.Id, id, Ct);
            await Assert.ThrowsAsync<AiServiceException>(() => service.EvaluateAsync(owner.Id, id, null, Ct));
        }
        await using (var db = sql.Context())
        {
            Assert.Equal(firstTurn, (await db.TurnEvaluations.SingleAsync()).TurnId);
            Assert.Equal(5, await db.TurnScores.CountAsync());
            Assert.Empty(await db.Evaluations.ToListAsync());
            var session = await db.Sessions.SingleAsync(x => x.Id == id);
            Assert.Equal(SessionStatus.ReadyForEvaluation, session.Status);
            Assert.Null(session.OperationId);
            Assert.Null(session.BusyUntil);
            await Service(db, ai).EvaluateAsync(owner.Id, id, null, Ct);
        }
        await using var verify = sql.Context();
        var report = await verify.Evaluations.SingleAsync();
        Assert.Equal(3.2m, report.OverallScore);
        Assert.Equal(32, report.ReadinessScore);
        Assert.Equal(40m, report.CompletionRate);
        Assert.True(report.IsProvisional);
        Assert.Equal("No Hire", report.Verdict);
        Assert.Equal("scripted-test-only", report.Model);
        Assert.Equal(2, await verify.TurnEvaluations.CountAsync());
        Assert.Equal(10, await verify.TurnScores.CountAsync());
        Assert.Equal(SessionStatus.Completed, (await verify.Sessions.SingleAsync()).Status);
        Assert.Equal(3, ai.Requests.Count(x => x.Operation == "evaluate-answer"));
        var gradedQuestions = ai.Requests.Where(x => x.Operation == "evaluate-answer")
            .Select(x => JsonSerializer.SerializeToElement(x.Data).GetProperty("question").GetString()).ToArray();
        Assert.Equal(new[] { "Explain atomicity.", "Explain durability.", "Explain durability." }, gradedQuestions);
        var requestCount = ai.Requests.Count;
        await Service(verify, ai).EvaluateAsync(owner.Id, id, null, Ct);
        Assert.Equal(requestCount, ai.Requests.Count);
        Assert.Equal(1, await verify.Evaluations.CountAsync());
    }

    [SqlFact]
    public async Task SqlServerEnforcesForeignKeysUniqueIndexesChecksAndRowVersion()
    {
        await using var sql = await SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var ai = new ScriptedAi();
        var service = Service(db, ai);
        var id = await service.CreateAsync(owner.Id, await Input(db), Ct);
        await Reject(db, new CvProfile { StudentId = "missing-identity-user", FileName = "cv.pdf", ExtractedText = "text" });
        var positionName = await db.Positions.Select(x => x.Name).FirstAsync();
        await Reject(db, new Position { Name = positionName });
        await Reject(db, new RubricVersion { Name = "Second active rubric", IsActive = true });
        var saved = await db.Sessions.SingleAsync(x => x.Id == id);
        var originalCount = saved.PlannedQuestionCount;
        saved.PlannedQuestionCount = 4;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(originalCount, (await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == id)).PlannedQuestionCount);
        await Reject(db, new InterviewTurn { SessionId = id, SequenceNumber = 1, MainQuestionNumber = 1, FollowUpDepth = 3, Question = "Invalid depth" });
        var turn = new InterviewTurn { SessionId = id, SequenceNumber = 1, MainQuestionNumber = 1, Question = "Valid turn" };
        db.Turns.Add(turn);
        await db.SaveChangesAsync();
        await Reject(db, new InterviewTurn { SessionId = id, SequenceNumber = 1, MainQuestionNumber = 2, Question = "Duplicate sequence" });
        await Reject(db, new TurnEvaluation { TurnId = turn.Id, OverallScore = 10.01m });
        await Reject(db, new DeliveryMetric { TurnId = turn.Id, WordCount = -1 });

        await using var first = sql.Context();
        await using var second = sql.Context();
        var firstSession = await first.Sessions.SingleAsync(x => x.Id == id);
        var stale = await second.Sessions.SingleAsync(x => x.Id == id);
        var previousRowVersion = firstSession.RowVersion.ToArray();
        firstSession.FocusSkills = "First writer";
        await first.SaveChangesAsync();
        Assert.False(previousRowVersion.SequenceEqual(firstSession.RowVersion));
        stale.FocusSkills = "Stale writer";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var verify = sql.Context();
        Assert.Equal("First writer", (await verify.Sessions.SingleAsync(x => x.Id == id)).FocusSkills);
    }

    private static async Task Reject(AppDbContext db, object invalid)
    {
        db.Add(invalid);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
    }

    private static InterviewService Service(AppDbContext db, IAiService ai) =>
        new(db, ai, new DeliveryAnalyticsService(), Options.Create(new GeminiOptions()));

    private static async Task<CreateInterviewModel> Input(AppDbContext db) => new()
    {
        PositionId = await db.Positions.Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync(),
        ConsentToAi = true, ConsentedProvider = "Gemini", QuestionCount = 5, Kind = InterviewKind.Technical
    };

    private static async Task<(CvProfile Cv, JobDescription Jd)> AddDocuments(AppDbContext db, string owner)
    {
        var cv = new CvProfile { StudentId = owner, FileName = "student.pdf", ExtractedText = "I built a C# web application with SQL Server." };
        var jd = new JobDescription { StudentId = owner, Title = "Backend internship", Content = "Build and test C# applications backed by SQL Server." };
        db.AddRange(cv, jd);
        await db.SaveChangesAsync();
        return (cv, jd);
    }

    private static ControllerContext ControllerFor(string user) => new()
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Role, Roles.Student)], "TestIdentity")) }
    };

    private static QuestionResult Question(string text, bool followUp = false) =>
        new() { Question = text, FollowUp = followUp, SkillTags = ["Testing"] };

    private static object EvaluateAnswer(AiRequest request)
    {
        Assert.Equal("evaluate-answer", request.Operation);
        var data = JsonSerializer.SerializeToElement(request.Data);
        var answer = data.GetProperty("answer").GetString()!;
        return new ReferencedAnswerEvaluation
        {
            Scores = data.GetProperty("criteria").EnumerateArray().Select(c => new ReferencedCriterionResult
            {
                Code = c.GetProperty("Code").GetString()!, Score = 8,
                Feedback = "Relevant explanation with evidence.", EvidenceId = data.GetProperty("evidenceChoices")[0].GetProperty("Id").GetString()!
            }).ToArray(),
            Strengths = ["States the central property."], Improvements = ["Add a concrete failure example."],
            BetterAnswer = answer + " Add an explicitly hypothetical example.", ChangesExplanation = "Added a suggestion for depth."
        };
    }

    private static object Summarize(AiRequest request)
    {
        Assert.Equal("evaluate-session", request.Operation);
        var data = JsonSerializer.SerializeToElement(request.Data);
        return new ReferencedSessionSummary
        {
            Verdict = data.GetProperty("expectedVerdict").GetString()!,
            Reason = "Provisional educational result: only two of five main questions were answered.",
            Strengths = ["Relevant technical statements."], Weaknesses = ["Interview ended early."],
            PracticePlan = ["Complete five questions.", "Give a concrete example.", "Explain one tradeoff."],
            Evidence = [new ReferencedSummaryEvidence
            {
                EvidenceId = data.GetProperty("evidenceChoices")[0].GetProperty("Id").GetString()!, Explanation = "The saved answer states the property."
            }]
        };
    }

    private static IConfiguration SeedConfiguration(bool withAccounts = false, string? adminEmail = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:Enabled"] = "true", ["Seed:Password"] = withAccounts ? SqlDatabase.Password : null,
            ["Seed:AdminEmail"] = adminEmail ?? "admin@example.test"
        }).Build();

    private sealed class ScriptedAi : IAiService
    {
        private readonly Queue<Func<AiRequest, object>> steps = new();
        public string Provider { get; init; } = "Gemini";
        public bool IsConfigured => true;
        public string Model => "scripted-test-only";
        public List<AiRequest> Requests { get; } = [];
        public void Enqueue(object result) => steps.Enqueue(_ => result);
        public void Enqueue(Func<AiRequest, object> step) => steps.Enqueue(step);
        public void EnqueueFailure() => steps.Enqueue(_ => throw new AiServiceException("test_failure", "Scripted provider failure."));

        public Task<T> GenerateAsync<T>(AiRequest request, Func<T, string?> validate,
            Func<string, Task>? onText = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            Assert.NotEmpty(steps);
            var result = Assert.IsType<T>(steps.Dequeue()(request));
            var error = validate(result);
            if (error is not null) throw new AiServiceException("invalid_test_response", error);
            return Task.FromResult(result);
        }
    }

    /// <summary>Creates only a new GUID database; deletion is limited to this exact owned database.</summary>
    internal sealed class SqlDatabase : IAsyncDisposable
    {
        public const string Password = "SqlIntegration-Only!2026";
        private const string Prefix = "AiInterview_Test_";
        private const string Server = @"(localdb)\AiInterviewDev";
        private readonly string name = Prefix + Guid.NewGuid().ToString("N");
        private bool createdByThisFixture;
        private string ConnectionString => new SqlConnectionStringBuilder
        {
            DataSource = Server, InitialCatalog = name, IntegratedSecurity = true,
            TrustServerCertificate = true, ConnectTimeout = 30, Pooling = false
        }.ConnectionString;
        private string MasterConnectionString => new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        public ServiceProvider Services { get; private set; } = null!;

        public AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

        public static async Task<SqlDatabase> CreateAsync()
        {
            if (Environment.GetEnvironmentVariable("AIINTERVIEW_TEST_SQL") != "1")
                throw new InvalidOperationException("SQL tests require explicit opt-in.");
            var fixture = new SqlDatabase();
            try
            {
                fixture.ValidateOwnedName();
                await using (var connection = new SqlConnection(fixture.MasterConnectionString))
                {
                    await connection.OpenAsync();
                    await using var exists = new SqlCommand("SELECT DB_ID(@name)", connection);
                    exists.Parameters.AddWithValue("@name", fixture.name);
                    if (await exists.ExecuteScalarAsync() is not DBNull)
                        throw new InvalidOperationException("Refusing to use any pre-existing database.");
                    await using var create = new SqlCommand($"CREATE DATABASE [{fixture.name}]", connection);
                    await create.ExecuteNonQueryAsync();
                    fixture.createdByThisFixture = true;
                }
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddDbContext<AppDbContext>(options => options.UseSqlServer(fixture.ConnectionString));
                services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
                    .AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDbContext>();
                fixture.Services = services.BuildServiceProvider();
                await using (var db = fixture.Context()) await db.Database.MigrateAsync();
                await DatabaseSeeder.SeedAsync(fixture.Services, SeedConfiguration(), development: false);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async Task<ApplicationUser> CreateStudentAsync(string? email = null)
        {
            await using var scope = Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            email ??= $"student-{Guid.NewGuid():N}@example.test";
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = "Integration student" };
            var created = await users.CreateAsync(user, Password);
            Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(x => x.Code)));
            var assigned = await users.AddToRoleAsync(user, Roles.Student);
            Assert.True(assigned.Succeeded, string.Join(", ", assigned.Errors.Select(x => x.Code)));
            return user;
        }

        private void ValidateOwnedName()
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length != Prefix.Length + 32
                || !Guid.TryParseExact(name[Prefix.Length..], "N", out _)
                || new SqlConnectionStringBuilder(ConnectionString).InitialCatalog != name
                || new SqlConnectionStringBuilder(ConnectionString).DataSource != Server)
                throw new InvalidOperationException("Unsafe integration database target.");
        }

        public async ValueTask DisposeAsync()
        {
            if (Services is not null) await Services.DisposeAsync();
            if (!createdByThisFixture) return;
            ValidateOwnedName();
            await using var connection = new SqlConnection(MasterConnectionString);
            await connection.OpenAsync();
            await using var drop = new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];", connection);
            await drop.ExecuteNonQueryAsync();
            createdByThisFixture = false;
        }
    }
}
