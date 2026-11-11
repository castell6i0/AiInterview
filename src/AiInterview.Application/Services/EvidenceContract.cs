using System.Text.Json;
using System.Text.Json.Nodes;
using AiInterview.Application.DTOs;
using AiInterview.Domain.Entities;

namespace AiInterview.Application.Services;

public sealed record EvidenceReference(string Id, int SequenceNumber, string Quote);
public sealed class ReferencedCriterionResult
{
    public string Code { get; set; } = "";
    public decimal Score { get; set; }
    public string Feedback { get; set; } = "";
    public string EvidenceId { get; set; } = "";
}
public sealed class ReferencedAnswerEvaluation
{
    public ReferencedCriterionResult[] Scores { get; set; } = [];
    public string[] Strengths { get; set; } = [];
    public string[] Improvements { get; set; } = [];
    public string BetterAnswer { get; set; } = "";
    public string ChangesExplanation { get; set; } = "";
    public CodeReviewResult CodeReview { get; set; } = new();
}
public sealed class ReferencedSummaryEvidence
{
    public string EvidenceId { get; set; } = "";
    public string Explanation { get; set; } = "";
}
public sealed class ReferencedSessionSummary
{
    public string Verdict { get; set; } = "";
    public string Reason { get; set; } = "";
    public string[] Strengths { get; set; } = [];
    public string[] Weaknesses { get; set; } = [];
    public string[] PracticePlan { get; set; } = [];
    public ReferencedSummaryEvidence[] Evidence { get; set; } = [];
}

/// <summary>Models select short IDs. Only the server copies quotes and their turn numbers from saved answers.</summary>
public sealed class EvidenceContract
{
    private readonly Dictionary<string, EvidenceReference> references;
    public IReadOnlyList<EvidenceReference> Choices { get; }

    private EvidenceContract(IEnumerable<EvidenceReference> choices)
    {
        Choices = choices.ToArray();
        if (Choices.Count == 0) throw new ArgumentException("At least one nonempty answer is required.", nameof(choices));
        references = Choices.ToDictionary(x => x.Id, StringComparer.Ordinal);
    }

    public static EvidenceContract ForAnswer(string answer, int sequenceNumber) => new(
        AiPrompts.EvidenceOptions(answer).Select((quote, index) => new EvidenceReference($"t{sequenceNumber:D2}e{index + 1:D2}", sequenceNumber, quote)));

    public static EvidenceContract ForSummary(IReadOnlyCollection<InterviewTurn> answered) => new(
        answered.OrderBy(t => t.SequenceNumber).SelectMany(t =>
        {
            // Prefer the quotations already used by the five saved criteria. Keep at most three
            // representative excerpts per turn, so a 30-turn session has at most 90 short enum IDs.
            var saved = t.Evaluation?.Scores.Select(s => s.Evidence) ?? [];
            var quotes = saved.Concat(AiPrompts.EvidenceOptions(t.Answer!))
                .Where(q => !string.IsNullOrWhiteSpace(q) && q.Length <= 500 && t.Answer!.Contains(q, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal).Take(3);
            return quotes.Select((quote, index) => new EvidenceReference($"t{t.SequenceNumber:D2}e{index + 1:D2}", t.SequenceNumber, quote));
        }));

    private JsonNode IdSchema() => JsonSerializer.SerializeToNode(new { type = "string", @enum = Choices.Select(x => x.Id).ToArray() })!;

    public object EvaluationSchema(IReadOnlyCollection<RubricCriterion> criteria)
    {
        var schema = JsonSerializer.SerializeToNode(AiPrompts.Schema<AnswerEvaluationResult>())!;
        var scores = schema["properties"]!["scores"]!;
        scores["minItems"] = criteria.Count;
        scores["maxItems"] = criteria.Count;
        var item = scores["items"]!;
        var fields = item["properties"]!.AsObject();
        fields.Remove("evidence");
        fields["evidenceId"] = IdSchema();
        fields["code"] = JsonSerializer.SerializeToNode(new { type = "string", @enum = criteria.Select(c => c.Code).ToArray() });
        item["required"] = JsonSerializer.SerializeToNode(fields.Select(x => x.Key).ToArray());
        return schema;
    }

    public object SummarySchema(string expectedVerdict)
    {
        var schema = JsonSerializer.SerializeToNode(AiPrompts.Schema<SessionSummaryResult>())!;
        schema["properties"]!["verdict"]!["enum"] = JsonSerializer.SerializeToNode(new[] { expectedVerdict });
        var evidence = schema["properties"]!["evidence"]!;
        evidence["minItems"] = 1;
        evidence["maxItems"] = 20;
        var item = evidence["items"]!;
        var fields = item["properties"]!.AsObject();
        fields.Remove("sequenceNumber");
        fields.Remove("quote");
        fields["evidenceId"] = IdSchema();
        item["required"] = JsonSerializer.SerializeToNode(fields.Select(x => x.Key).ToArray());
        schema["properties"]!["practicePlan"]!["minItems"] = 3;
        schema["properties"]!["practicePlan"]!["maxItems"] = 5;
        return schema;
    }

    public AnswerEvaluationResult Resolve(ReferencedAnswerEvaluation result) => new()
    {
        Scores = result.Scores.Select(s => new CriterionResult { Code = s.Code, Score = s.Score, Feedback = s.Feedback, Evidence = Reference(s.EvidenceId).Quote }).ToArray(),
        Strengths = result.Strengths, Improvements = result.Improvements, BetterAnswer = result.BetterAnswer,
        ChangesExplanation = result.ChangesExplanation, CodeReview = result.CodeReview
    };
    public SessionSummaryResult Resolve(ReferencedSessionSummary result) => new()
    {
        Verdict = result.Verdict, Reason = result.Reason, Strengths = result.Strengths, Weaknesses = result.Weaknesses,
        PracticePlan = result.PracticePlan,
        Evidence = result.Evidence.Select(e => new SummaryEvidence { SequenceNumber = Reference(e.EvidenceId).SequenceNumber,
            Quote = Reference(e.EvidenceId).Quote, Explanation = e.Explanation }).ToArray()
    };
    private EvidenceReference Reference(string id) => id is not null && references.TryGetValue(id, out var reference)
        ? reference : throw new ArgumentException("Unknown evidence reference.", nameof(id));

    public string? Validate(ReferencedAnswerEvaluation result, IReadOnlyCollection<RubricCriterion> criteria, string answer, bool isCode)
    {
        if (result?.Scores is null || result.Scores.Any(s => s is null || s.EvidenceId is null || !references.ContainsKey(s.EvidenceId))) return "Unknown evaluation evidence reference";
        return AiPrompts.ValidateEvaluation(Resolve(result), criteria, answer, isCode);
    }
    public string? Validate(ReferencedSessionSummary result, string expectedVerdict, IReadOnlyCollection<InterviewTurn> answered)
    {
        if (result?.Evidence is null || result.Evidence.Any(e => e is null || e.EvidenceId is null || !references.ContainsKey(e.EvidenceId))) return "Unknown summary evidence reference";
        return AiPrompts.ValidateSummary(Resolve(result), expectedVerdict, answered);
    }
}
