namespace AiInterview.Application.DTOs;

public sealed class CvAnalysis
{
    public string Summary { get; set; } = "";
    public string[] Skills { get; set; } = [];
    public string[] Projects { get; set; } = [];
    public string[] Experience { get; set; } = [];
}
public sealed class SkillGapAnalysis
{
    public string[] MatchedSkills { get; set; } = [];
    public string[] MissingEvidence { get; set; } = [];
    public string[] Recommendations { get; set; } = [];
}
public sealed class QuestionResult
{
    public string Question { get; set; } = "";
    public string[] SkillTags { get; set; } = [];
    public bool FollowUp { get; set; }
    public bool Finished { get; set; }
    public bool IsCoding { get; set; }
    public string CodeLanguage { get; set; } = "";
}
public sealed class CriterionResult
{
    public string Code { get; set; } = "";
    public decimal Score { get; set; }
    public string Feedback { get; set; } = "";
    public string Evidence { get; set; } = "";
}
public sealed class CodeReviewResult
{
    public string Correctness { get; set; } = "";
    public string Complexity { get; set; } = "";
    public string[] EdgeCases { get; set; } = [];
    public string Style { get; set; } = "";
}
public sealed class AnswerEvaluationResult
{
    public CriterionResult[] Scores { get; set; } = [];
    public string[] Strengths { get; set; } = [];
    public string[] Improvements { get; set; } = [];
    public string BetterAnswer { get; set; } = "";
    public string ChangesExplanation { get; set; } = "";
    public CodeReviewResult CodeReview { get; set; } = new();
}
public sealed class SummaryEvidence
{
    public int SequenceNumber { get; set; }
    public string Quote { get; set; } = "";
    public string Explanation { get; set; } = "";
}
public sealed class SessionSummaryResult
{
    public string Verdict { get; set; } = "";
    public string Reason { get; set; } = "";
    public string[] Strengths { get; set; } = [];
    public string[] Weaknesses { get; set; } = [];
    public string[] PracticePlan { get; set; } = [];
    public SummaryEvidence[] Evidence { get; set; } = [];
}
public sealed class SpeechTelemetry
{
    public bool UsedRecognition { get; set; }
    public double? FirstSpeechMs { get; set; }
    public double? ElapsedMs { get; set; }
    public List<SpeechWindow> Windows { get; set; } = [];
}
public sealed class SpeechWindow
{
    public double StartMs { get; set; }
    public double EndMs { get; set; }
}
