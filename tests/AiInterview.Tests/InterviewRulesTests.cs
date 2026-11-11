using AiInterview.Application.DTOs;
using AiInterview.Application.Services;
using AiInterview.Domain.Entities;
using Xunit;

namespace AiInterview.Tests;

public class InterviewRulesTests
{
    private const string Answer = "Tôi dùng dependency injection để thay implementation khi kiểm thử.";
    private static readonly RubricCriterion[] Criteria =
    [
        new() { Code = "accuracy", Weight = 30 }, new() { Code = "depth", Weight = 25 },
        new() { Code = "structure", Weight = 20 }, new() { Code = "relevance", Weight = 15 },
        new() { Code = "language", Weight = 10 }
    ];

    private static AnswerEvaluationResult ValidResult() => new()
    {
        Scores = Criteria.Select(c => new CriterionResult
        { Code = c.Code, Score = 7, Feedback = "Có ví dụ nhưng cần giải thích lifetime.", Evidence = "dependency injection" }).ToArray(),
        Strengths = ["Nêu được ứng dụng."], Improvements = ["Giải thích scope và lifetime."],
        BetterAnswer = "Dependency injection giúp tách phụ thuộc qua interface để thay implementation khi kiểm thử.",
        ChangesExplanation = "Bổ sung mục đích của interface và giải thích rõ hơn." ,
        CodeReview = new CodeReviewResult()
    };

    [Fact]
    public void AllRubricCriteriaWithVerbatimEvidenceAreAcceptedRegardlessOfOrder()
    {
        var result = ValidResult();
        result.Scores = result.Scores.Reverse().ToArray();
        Assert.Null(AiPrompts.ValidateEvaluation(result, Criteria, Answer));
    }

    [Fact]
    public void MissingCriterionIsRejected()
    {
        var result = ValidResult(); result.Scores = result.Scores[..^1];
        Assert.NotNull(AiPrompts.ValidateEvaluation(result, Criteria, Answer));
    }

    [Fact]
    public void DuplicateCriterionCannotReplaceAnotherCriterion()
    {
        var result = ValidResult(); result.Scores[4].Code = result.Scores[0].Code;
        Assert.NotNull(AiPrompts.ValidateEvaluation(result, Criteria, Answer));
    }

    [Fact]
    public void UnknownCriterionIsRejectedEvenWhenCountMatches()
    {
        var result = ValidResult(); result.Scores[4].Code = "unrequested";
        Assert.NotNull(AiPrompts.ValidateEvaluation(result, Criteria, Answer));
    }

    [Theory]
    [InlineData("Dependency Injection")]
    [InlineData("Tôi ... implementation")]
    [InlineData("tôi đã làm việc ba năm")]
    [InlineData("")]
    public void InferredParaphrasedOrCaseChangedEvidenceIsRejected(string evidence)
    {
        var result = ValidResult(); result.Scores[0].Evidence = evidence;
        Assert.NotNull(AiPrompts.ValidateEvaluation(result, Criteria, Answer));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(10.1)]
    public void OutOfRangeScoresAreRejected(double score)
    {
        var result = ValidResult(); result.Scores[0].Score = (decimal)score;
        Assert.NotNull(AiPrompts.ValidateEvaluation(result, Criteria, Answer));
    }

    [Fact]
    public void NullScoreElementIsAValidationFailureRatherThanAnException()
    {
        var result = ValidResult(); result.Scores[0] = null!;
        Assert.NotNull(AiPrompts.ValidateEvaluation(result, Criteria, Answer));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyImprovedAnswerOrExplanationIsRejected(bool blankAnswer)
    {
        var result = ValidResult();
        if (blankAnswer) result.BetterAnswer = " "; else result.ChangesExplanation = " ";
        Assert.NotNull(AiPrompts.ValidateEvaluation(result, Criteria, Answer));
    }

    [Fact]
    public void WeightedScoreNormalizesWeightsAndDoesNotUsePlainAverage()
    {
        Assert.Equal(8m, AiPrompts.WeightedScore([(10m, 3m), (2m, 1m)]));
        Assert.Equal(8m, AiPrompts.WeightedScore([(10m, 30m), (2m, 10m)]));
    }

    [Fact]
    public void WeightedScoreRoundsMidpointsAwayFromZero()
        => Assert.Equal(7.01m, AiPrompts.WeightedScore([(7.005m, 1m)]));

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(11, 1)]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    public void InvalidWeightsOrScoresCannotProduceAReport(double score, double weight)
        => Assert.Throws<ArgumentException>(() => AiPrompts.WeightedScore([((decimal)score, (decimal)weight)]));

    [Fact]
    public void EmptyScoresCannotProduceAReport()
        => Assert.Throws<ArgumentException>(() => AiPrompts.WeightedScore([]));

    [Theory]
    [InlineData(0, "No Hire")]
    [InlineData(4.99, "No Hire")]
    [InlineData(5, "Lean No Hire")]
    [InlineData(6.99, "Lean No Hire")]
    [InlineData(7, "Hire")]
    [InlineData(8.49, "Hire")]
    [InlineData(8.5, "Strong Hire")]
    [InlineData(10, "Strong Hire")]
    public void VerdictBoundariesAreDeterministic(double score, string verdict)
        => Assert.Equal(verdict, AiPrompts.Verdict((decimal)score));
}
