using AiInterview.Domain.Entities;

namespace AiInterview.Infrastructure.Persistence;

/// <summary>UTF-16 code-unit bounds, including trailing spaces. JSON allows worst-case escaping.</summary>
public static class DatabaseTextLimits
{
    public sealed record ColumnLimit(Type Entity, string Table, string Column, int Characters)
    {
        public string Constraint => $"CK_{Table}_{Column}_Length";
        public string Predicate => $"[{Column}] IS NULL OR DATALENGTH([{Column}]) <= {Characters * 2L}";
    }

    public static IReadOnlyList<ColumnLimit> All { get; } =
    [
        new(typeof(CvProfile), "CvProfiles", "ExtractedText", 50_000),
        new(typeof(CvProfile), "CvProfiles", "AnalysisJson", 5_000_000),
        new(typeof(JobDescription), "JobDescriptions", "Content", 30_000),
        new(typeof(JobDescription), "JobDescriptions", "SkillGapJson", 4_500_000),
        new(typeof(InterviewSession), "Sessions", "CvSnapshot", 50_000),
        new(typeof(InterviewSession), "Sessions", "JdSnapshot", 30_000),
        new(typeof(InterviewSession), "Sessions", "SkillGapSnapshotJson", 4_500_000),
        new(typeof(InterviewTurn), "Turns", "Question", 5_000),
        new(typeof(InterviewTurn), "Turns", "Answer", 20_000),
        new(typeof(InterviewTurn), "Turns", "DraftAnswer", 20_000),
        new(typeof(InterviewTurn), "Turns", "SpeechEventsJson", 1_000_000),
        new(typeof(TurnEvaluation), "TurnEvaluations", "StrengthsJson", 150_000),
        new(typeof(TurnEvaluation), "TurnEvaluations", "ImprovementsJson", 150_000),
        new(typeof(TurnEvaluation), "TurnEvaluations", "BetterAnswer", 20_000),
        new(typeof(TurnEvaluation), "TurnEvaluations", "ChangesExplanation", 8_000),
        new(typeof(TurnEvaluation), "TurnEvaluations", "CodeReviewJson", 250_000),
        new(typeof(TurnScore), "TurnScores", "Feedback", 5_000),
        new(typeof(TurnScore), "TurnScores", "Evidence", 2_000),
        new(typeof(Evaluation), "Evaluations", "Reason", 8_000),
        new(typeof(Evaluation), "Evaluations", "SummaryJson", 1_100_000),
        new(typeof(DeliveryMetric), "DeliveryMetrics", "FillerDetailsJson", 20_000),
        new(typeof(QuestionBank), "Questions", "Content", 4_000),
        new(typeof(Assignment), "Assignments", "Instructions", 4_000),
        new(typeof(LecturerComment), "LecturerComments", "Content", 3_000)
    ];
}
