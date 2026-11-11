using System.Text.Json;
using AiInterview.Application.DTOs;
using AiInterview.Application.Interfaces;
using AiInterview.Application.Services;
using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.AI;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiInterview.Tests;

/// <summary>Real SQL persistence tests. The provider is scripted only inside this test file.</summary>
public sealed class SessionRecoverySqlTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [SqlFact]
    public async Task FifthMainAnswerFinishesWithoutCallingAnUnavailableProvider()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var ai = new TestAi { IsConfigured = false };
        var service = Service(db, ai);
        var id = await service.CreateAsync(owner.Id, await Input(db), Ct);
        var fifth = await AddTurns(db, id, 5, lastPending: true);

        await service.AnswerAsync(owner.Id, id, new SubmitAnswerModel
        { TurnId = fifth.Id, Answer = "I would run the highest-risk regression scenarios first." }, Ct);
        Assert.Null(await service.GenerateNextAsync(owner.Id, id, null, Ct));
        Assert.Empty(ai.Requests);

        await using var verify = sql.Context();
        var saved = await verify.Sessions.Include(s => s.Turns).SingleAsync(s => s.Id == id);
        Assert.Equal(SessionStatus.ReadyForEvaluation, saved.Status);
        Assert.Equal(5, saved.Turns.Count);
        Assert.All(saved.Turns, t => Assert.Equal(TurnStatus.Answered, t.Status));
        Assert.NotNull(saved.EndedAt);
        Assert.False(saved.EndedEarly);
        Assert.Null(saved.OperationId);
        Assert.Null(saved.BusyUntil);

        var endedAt = saved.EndedAt;
        Assert.Null(await service.GenerateNextAsync(owner.Id, id, null, Ct));
        await verify.Entry(saved).ReloadAsync();
        Assert.Equal(endedAt, saved.EndedAt);
        Assert.Empty(ai.Requests);
    }

    [SqlFact]
    public async Task FifthPendingMainQuestionRemainsAnswerableInsteadOfFinishing()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var ai = new TestAi { IsConfigured = false };
        var service = Service(db, ai);
        var id = await service.CreateAsync(owner.Id, await Input(db), Ct);
        var fifth = await AddTurns(db, id, 5, lastPending: true);

        var next = await service.GenerateNextAsync(owner.Id, id, null, Ct);

        Assert.NotNull(next);
        Assert.Equal(fifth.Id, next.Id);
        Assert.Equal(TurnStatus.Pending, next.Status);
        Assert.Empty(ai.Requests);
        await using var verify = sql.Context();
        var saved = await verify.Sessions.SingleAsync(s => s.Id == id);
        Assert.Equal(SessionStatus.InProgress, saved.Status);
        Assert.Null(saved.EndedAt);
        Assert.Null(saved.OperationId);
        Assert.Null(saved.BusyUntil);
    }

    [SqlFact]
    public async Task NewSessionsPersistTheSelectedProvider()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var gemini = new TestAi();
        var openAi = new TestAi { Provider = "OpenAI" };
        var geminiId = await Service(db, gemini).CreateAsync(owner.Id, await Input(db), Ct);
        var openAiId = await Service(db, openAi).CreateAsync(owner.Id, await Input(db, "OpenAI"), Ct);

        await using var verify = sql.Context();
        Assert.Equal("Gemini", (await verify.Sessions.SingleAsync(s => s.Id == geminiId)).AiProvider);
        Assert.Equal("OpenAI", (await verify.Sessions.SingleAsync(s => s.Id == openAiId)).AiProvider);
        Assert.Empty(gemini.Requests);
        Assert.Empty(openAi.Requests);
    }

    [SqlFact]
    public async Task SwitchingProviderRequiresTheOwnersExplicitCurrentProviderConsentBeforeGeneration()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        var other = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var id = await Service(db, new TestAi()).CreateAsync(owner.Id, await Input(db), Ct);
        var openAi = new TestAi { Provider = "OpenAI" };
        var service = Service(db, openAi);
        openAi.Enqueue(new QuestionResult { Question = "How would you investigate an intermittent API test failure?", SkillTags = ["Testing"] });

        var blocked = await Assert.ThrowsAsync<AiServiceException>(() => service.GenerateNextAsync(owner.Id, id, null, Ct));
        Assert.Equal("provider_changed", blocked.Code);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ConsentToProviderAsync(other.Id, id, "OpenAI", Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConsentToProviderAsync(owner.Id, id, "Gemini", Ct));
        Assert.Empty(openAi.Requests);
        await using (var verify = sql.Context())
        {
            var saved = await verify.Sessions.SingleAsync(s => s.Id == id);
            Assert.Equal("Gemini", saved.AiProvider);
            Assert.Equal(SessionStatus.Created, saved.Status);
            Assert.Null(saved.OperationId);
            Assert.Null(saved.BusyUntil);
            Assert.False(await verify.Turns.AnyAsync(t => t.SessionId == id));
        }

        await service.ConsentToProviderAsync(owner.Id, id, "OpenAI", Ct);
        Assert.Empty(openAi.Requests);
        var turn = await service.GenerateNextAsync(owner.Id, id, null, Ct);

        Assert.NotNull(turn);
        Assert.Equal("question", Assert.Single(openAi.Requests).Operation);
        await using var final = sql.Context();
        Assert.Equal("OpenAI", (await final.Sessions.SingleAsync(s => s.Id == id)).AiProvider);
        Assert.Equal(turn.Id, (await final.Turns.SingleAsync(t => t.SessionId == id)).Id);
    }

    [SqlFact]
    public async Task SwitchingProviderDoesNotSendSavedAnswersForEvaluationUntilExplicitConsent()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var gemini = Service(db, new TestAi());
        var id = await gemini.CreateAsync(owner.Id, await Input(db), Ct);
        var turn = await AddTurns(db, id, 1, lastPending: false);
        await gemini.EndAsync(owner.Id, id, Ct);
        var openAi = new TestAi { Provider = "OpenAI" };
        openAi.Enqueue(EvaluateAnswer);
        openAi.Enqueue(Summarize);
        var service = Service(db, openAi);

        var blocked = await Assert.ThrowsAsync<AiServiceException>(() => service.EvaluateAsync(owner.Id, id, null, Ct));
        Assert.Equal("provider_changed", blocked.Code);
        Assert.Empty(openAi.Requests);
        await using (var verify = sql.Context())
        {
            var saved = await verify.Sessions.Include(s => s.Evaluation).Include(s => s.Turns).ThenInclude(t => t.Evaluation).SingleAsync(s => s.Id == id);
            Assert.Equal(SessionStatus.ReadyForEvaluation, saved.Status);
            Assert.Equal("Gemini", saved.AiProvider);
            Assert.Null(saved.Evaluation);
            Assert.Null(Assert.Single(saved.Turns).Evaluation);
            Assert.Equal(turn.Answer, Assert.Single(saved.Turns).Answer);
            Assert.Null(saved.OperationId);
        }

        await service.ConsentToProviderAsync(owner.Id, id, "OpenAI", Ct);
        await service.EvaluateAsync(owner.Id, id, null, Ct);

        Assert.Equal(new[] { "evaluate-answer", "evaluate-session" }, openAi.Requests.Select(r => r.Operation));
        await using var final = sql.Context();
        var completed = await final.Sessions.Include(s => s.Evaluation).Include(s => s.Turns).ThenInclude(t => t.Evaluation!).ThenInclude(e => e.Scores).SingleAsync(s => s.Id == id);
        Assert.Equal(SessionStatus.Completed, completed.Status);
        Assert.Equal("OpenAI", completed.AiProvider);
        Assert.Equal(openAi.Model, completed.Evaluation!.Model);
        Assert.Equal(turn.Answer, Assert.Single(completed.Turns).Answer);
        Assert.Equal(5, Assert.Single(completed.Turns).Evaluation!.Scores.Count);
        Assert.All(Assert.Single(completed.Turns).Evaluation!.Scores, score => Assert.Contains(score.Evidence, turn.Answer!));
    }

    [SqlFact]
    public async Task DuplicateQuestionFailureKeepsSavedAnswerAndAllowsRetryWithANewQuestion()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var ai = new TestAi();
        var service = Service(db, ai);
        var id = await service.CreateAsync(owner.Id, await Input(db), Ct);
        var previous = await AddTurns(db, id, 1, lastPending: false);
        ai.Enqueue(new QuestionResult { Question = previous.Question, SkillTags = ["Testing"] });
        ai.Enqueue(new QuestionResult { Question = "How would you isolate test data to avoid order-dependent failures?", SkillTags = ["Testing"] });

        var failure = await Assert.ThrowsAsync<AiServiceException>(() => service.GenerateNextAsync(owner.Id, id, null, Ct));
        Assert.Equal("invalid_test_response", failure.Code);
        await using (var verify = sql.Context())
        {
            var saved = await verify.Sessions.Include(s => s.Turns).SingleAsync(s => s.Id == id);
            Assert.Equal(SessionStatus.AwaitingQuestion, saved.Status);
            Assert.Equal(previous.Id, Assert.Single(saved.Turns).Id);
            Assert.Equal(previous.Answer, Assert.Single(saved.Turns).Answer);
            Assert.Null(saved.OperationId);
            Assert.Null(saved.BusyUntil);
            Assert.Null(saved.EndedAt);
        }

        var recovered = await service.GenerateNextAsync(owner.Id, id, null, Ct);
        Assert.NotNull(recovered);
        Assert.Equal(2, recovered.MainQuestionNumber);
        Assert.Equal(TurnStatus.Pending, recovered.Status);
        Assert.Equal(2, ai.Requests.Count);
        await using var final = sql.Context();
        Assert.Equal(2, await final.Turns.CountAsync(t => t.SessionId == id));
        Assert.Equal(SessionStatus.InProgress, (await final.Sessions.SingleAsync(s => s.Id == id)).Status);
    }

    private static InterviewService Service(AppDbContext db, IAiService ai) =>
        new(db, ai, new DeliveryAnalyticsService(), Options.Create(new GeminiOptions()));

    private static async Task<CreateInterviewModel> Input(AppDbContext db, string provider = "Gemini") => new()
    {
        PositionId = await db.Positions.Where(p => p.IsActive).OrderBy(p => p.Id).Select(p => p.Id).FirstAsync(),
        ConsentToAi = true, ConsentedProvider = provider, QuestionCount = 5, Kind = InterviewKind.Technical
    };

    private static async Task<InterviewTurn> AddTurns(AppDbContext db, Guid id, int count, bool lastPending)
    {
        var session = await db.Sessions.SingleAsync(s => s.Id == id);
        session.Status = lastPending ? SessionStatus.InProgress : SessionStatus.AwaitingQuestion;
        session.StartedAt = DateTimeOffset.UtcNow;
        InterviewTurn last = null!;
        for (var number = 1; number <= count; number++)
        {
            var pending = number == count && lastPending;
            last = new InterviewTurn
            {
                SessionId = id, SequenceNumber = number, MainQuestionNumber = number,
                Question = $"Describe one concrete testing scenario for requirement {number}.",
                SkillTags = "Testing", Status = pending ? TurnStatus.Pending : TurnStatus.Answered,
                Answer = pending ? null : "I would define the expected result and validate both boundary values.",
                SubmittedAt = pending ? null : DateTimeOffset.UtcNow
            };
            db.Turns.Add(last);
        }
        await db.SaveChangesAsync();
        return last;
    }

    private static object EvaluateAnswer(AiRequest request)
    {
        var data = JsonSerializer.SerializeToElement(request.Data);
        return new ReferencedAnswerEvaluation
        {
            Scores = data.GetProperty("criteria").EnumerateArray().Select(c => new ReferencedCriterionResult
            {
                Code = c.GetProperty("Code").GetString()!, Score = 7,
                Feedback = "Gives a concrete approach but lacks examples.",
                EvidenceId = data.GetProperty("evidenceChoices")[0].GetProperty("Id").GetString()!
            }).ToArray(),
            Strengths = ["States an explicit expected result."], Improvements = ["Add concrete input examples."],
            BetterAnswer = data.GetProperty("answer").GetString()! + " For example, use one valid and one invalid boundary value.",
            ChangesExplanation = "Added a hypothetical example to make the approach concrete."
        };
    }

    private static object Summarize(AiRequest request)
    {
        var data = JsonSerializer.SerializeToElement(request.Data);
        return new ReferencedSessionSummary
        {
            Verdict = data.GetProperty("expectedVerdict").GetString()!,
            Reason = "One answer provides limited evidence; the practice interview ended early.",
            Strengths = ["Identifies boundary values."], Weaknesses = ["Only one question was answered."],
            PracticePlan = ["Finish all main questions.", "Include concrete examples.", "Discuss one tradeoff."],
            Evidence = [new ReferencedSummaryEvidence
            { EvidenceId = data.GetProperty("evidenceChoices")[0].GetProperty("Id").GetString()!, Explanation = "The answer explicitly mentions boundary values." }]
        };
    }

    private sealed class TestAi : IAiService
    {
        private readonly Queue<Func<AiRequest, object>> steps = new();
        public string Provider { get; init; } = "Gemini";
        public bool IsConfigured { get; init; } = true;
        public string Model => "session-recovery-test-only";
        public List<AiRequest> Requests { get; } = [];
        public void Enqueue(object result) => steps.Enqueue(_ => result);
        public void Enqueue(Func<AiRequest, object> result) => steps.Enqueue(result);
        public Task<T> GenerateAsync<T>(AiRequest request, Func<T, string?> validate,
            Func<string, Task>? onText = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (!IsConfigured) throw new AiServiceException("missing_key", "Test provider is unavailable.");
            Assert.NotEmpty(steps);
            var result = Assert.IsType<T>(steps.Dequeue()(request));
            var error = validate(result);
            if (error is not null) throw new AiServiceException("invalid_test_response", error);
            return Task.FromResult(result);
        }
    }
}
