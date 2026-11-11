using System.Text.Json;
using AiInterview.Application.Services;
using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.AI;
using Xunit;

namespace AiInterview.Tests;

public sealed class CompactEvidenceTests
{
    private static readonly RubricCriterion[] Criteria = [new() { Code = "accuracy", Weight = 100 }];

    [Fact]
    public void LongAnswerDoesNotBecomeALargeEnumSchemaAndQuotesRemainExact()
    {
        var answer = string.Concat(Enumerable.Range(0, 200).Select(i => $"Sentence {i}: " + new string('x', 85) + ". "))[..20000];
        var catalog = EvidenceContract.ForAnswer(answer, 1);
        Assert.InRange(catalog.Choices.Count, 1, 80);
        Assert.All(catalog.Choices, evidence => Assert.Contains(evidence.Quote, answer, StringComparison.Ordinal));
        var schema = JsonSerializer.Serialize(catalog.EvaluationSchema(Criteria));
        Assert.True(schema.Length < 3000, $"Schema length was {schema.Length}.");
        Assert.DoesNotContain("Sentence", schema);
        var wire = Evaluation(catalog.Choices.Last().Id);
        Assert.True(StructuredOutputValidator.IsValid(AiPrompts.Json(wire), catalog.EvaluationSchema(Criteria)));
        Assert.Null(catalog.Validate(wire, Criteria, answer, false));
        Assert.Equal(catalog.Choices.Last().Quote, catalog.Resolve(wire).Scores[0].Evidence);
    }

    [Fact]
    public void ThirtyTurnSummaryKeepsSchemaBoundedAndResolvesCorrectTurnAutomatically()
    {
        var turns = Enumerable.Range(1, 30).Select(i => new InterviewTurn { SequenceNumber = i,
            Answer = $"Answer {i} starts here. Unique example {i}. Final line {i}. " + new string('x', 19900) }).ToArray();
        var catalog = EvidenceContract.ForSummary(turns);
        Assert.Equal(90, catalog.Choices.Count);
        var schema = JsonSerializer.Serialize(catalog.SummarySchema("Hire"));
        Assert.True(schema.Length < 3000);
        Assert.DoesNotContain("Answer 30", schema);
        var selected = catalog.Choices.Single(e => e.Id == "t30e02");
        var wire = Summary(selected.Id);
        Assert.True(StructuredOutputValidator.IsValid(AiPrompts.Json(wire), catalog.SummarySchema("Hire")));
        Assert.Null(catalog.Validate(wire, "Hire", turns));
        var report = catalog.Resolve(wire);
        Assert.Equal(30, report.Evidence[0].SequenceNumber);
        Assert.Equal(selected.Quote, report.Evidence[0].Quote);
        Assert.Contains(report.Evidence[0].Quote, turns[29].Answer!);
    }

    [Fact]
    public void UnknownOrFabricatedEvidenceCannotReachAReport()
    {
        var turn = new InterviewTurn { SequenceNumber = 2, Answer = "An actual saved answer." };
        var catalog = EvidenceContract.ForAnswer(turn.Answer, 2);
        var invented = Evaluation("t01e01");
        Assert.NotNull(catalog.Validate(invented, Criteria, turn.Answer, false));
        Assert.False(StructuredOutputValidator.IsValid(AiPrompts.Json(invented), catalog.EvaluationSchema(Criteria)));
        Assert.Throws<ArgumentException>(() => catalog.Resolve(invented));
        Assert.NotNull(catalog.Validate(Summary("t99e01"), "Hire", new[] { turn }));
    }

    [Fact]
    public void SchemaAndSemanticValidationRejectWrongCriterionCountsAndDuplicates()
    {
        var catalog = EvidenceContract.ForAnswer("Exact answer.", 1);
        var duplicate = Evaluation(catalog.Choices[0].Id);
        duplicate.Scores = [duplicate.Scores[0], duplicate.Scores[0]];
        Assert.False(StructuredOutputValidator.IsValid(AiPrompts.Json(duplicate), catalog.EvaluationSchema(Criteria)));
        Assert.NotNull(catalog.Validate(duplicate, Criteria, "Exact answer.", false));
    }

    [Fact]
    public void SummaryPrefersSavedCriteriaQuotesButRejectsFabricatedSavedQuotes()
    {
        var turn = new InterviewTurn { SequenceNumber = 1, Answer = "First sentence. Second sentence.",
            Evaluation = new TurnEvaluation { Scores = [new TurnScore { Evidence = "Second sentence." }, new TurnScore { Evidence = "Fabricated quote." }] } };
        var catalog = EvidenceContract.ForSummary(new[] { turn });
        Assert.Equal("Second sentence.", catalog.Choices[0].Quote);
        Assert.DoesNotContain(catalog.Choices, e => e.Quote.Contains("Fabricated"));
    }

    private static ReferencedAnswerEvaluation Evaluation(string evidenceId) => new()
    {
        Scores = [new() { Code = "accuracy", Score = 8, Feedback = "Supported answer.", EvidenceId = evidenceId }],
        BetterAnswer = "An example suggestion.", ChangesExplanation = "Add a concrete example."
    };
    private static ReferencedSessionSummary Summary(string evidenceId) => new()
    {
        Verdict = "Hire", Reason = "An educational estimate.", PracticePlan = ["Practice A", "Practice B", "Practice C"],
        Evidence = [new() { EvidenceId = evidenceId, Explanation = "Supported by this answer." }]
    };
}
