namespace AiInterview.Domain.Entities;

public static class Roles { public const string Student = "Student", Lecturer = "Lecturer", Admin = "Admin"; }
public enum InterviewLevel { Intern, Fresher }
public enum InterviewLanguage { Vietnamese, English }
public enum InterviewMode { Text, Voice }
public enum InterviewKind { Technical, Behavioral, Project, JobDescription, Mixed, Coding }
public enum InterviewPersona { Friendly, Strict, Challenger }
public enum SessionStatus { Created, InProgress, AwaitingQuestion, ReadyForEvaluation, Completed }
public enum TurnStatus { Pending, Answered, Skipped }

public class StudentProfile
{
    public string UserId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string University { get; set; } = "";
    public string Major { get; set; } = "";
    public int AcademicYear { get; set; } = 4;
}
public class Position
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public class CvProfile
{
    public int Id { get; set; }
    public string StudentId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ExtractedText { get; set; } = "";
    public string? AnalysisJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class JobDescription
{
    public int Id { get; set; }
    public string StudentId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Content { get; set; } = "";
    public string? SourceUrl { get; set; }
    public string? SkillGapJson { get; set; }
    public int? ComparedCvId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class RubricVersion
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<RubricCriterion> Criteria { get; set; } = [];
}
public class RubricCriterion
{
    public int Id { get; set; }
    public int RubricVersionId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Weight { get; set; }
}
public class InterviewSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StudentId { get; set; } = "";
    public int PositionId { get; set; }
    public Position Position { get; set; } = null!;
    public int? CvProfileId { get; set; }
    public int? JobDescriptionId { get; set; }
    public string CvSnapshot { get; set; } = "";
    public string JdSnapshot { get; set; } = "";
    public string? SkillGapSnapshotJson { get; set; }
    public string FocusSkills { get; set; } = "";
    public string AiProvider { get; set; } = "Gemini";
    public int RubricVersionId { get; set; }
    public RubricVersion RubricVersion { get; set; } = null!;
    public InterviewLevel Level { get; set; }
    public InterviewLanguage Language { get; set; }
    public InterviewMode Mode { get; set; }
    public InterviewKind Kind { get; set; }
    public InterviewPersona Persona { get; set; }
    public int PlannedQuestionCount { get; set; } = 5;
    public SessionStatus Status { get; set; } = SessionStatus.Created;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public bool EndedEarly { get; set; }
    public DateTimeOffset? BusyUntil { get; set; }
    public Guid? OperationId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<InterviewTurn> Turns { get; set; } = [];
    public Evaluation? Evaluation { get; set; }
}
public class InterviewTurn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public InterviewSession Session { get; set; } = null!;
    public int SequenceNumber { get; set; }
    public int MainQuestionNumber { get; set; }
    public Guid? RootTurnId { get; set; }
    public Guid? ParentTurnId { get; set; }
    public int FollowUpDepth { get; set; }
    public string Question { get; set; } = "";
    public string SkillTags { get; set; } = "";
    public bool IsCoding { get; set; }
    public string? CodeLanguage { get; set; }
    public string? Answer { get; set; }
    public string? DraftAnswer { get; set; }
    public TurnStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? QuestionDisplayedAt { get; set; }
    public DateTimeOffset? AnswerStartedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? SpeechEventsJson { get; set; }
    public bool Starred { get; set; }
    public bool NeedsPractice { get; set; }
    public TurnEvaluation? Evaluation { get; set; }
    public DeliveryMetric? DeliveryMetric { get; set; }
}
public class TurnEvaluation
{
    public Guid TurnId { get; set; }
    public decimal OverallScore { get; set; }
    public string StrengthsJson { get; set; } = "[]";
    public string ImprovementsJson { get; set; } = "[]";
    public string BetterAnswer { get; set; } = "";
    public string ChangesExplanation { get; set; } = "";
    public string? CodeReviewJson { get; set; }
    public List<TurnScore> Scores { get; set; } = [];
}
public class TurnScore
{
    public int Id { get; set; }
    public Guid TurnId { get; set; }
    public int RubricCriterionId { get; set; }
    public RubricCriterion Criterion { get; set; } = null!;
    public decimal Score { get; set; }
    public string Feedback { get; set; } = "";
    public string Evidence { get; set; } = "";
}
public class Evaluation
{
    public Guid SessionId { get; set; }
    public decimal OverallScore { get; set; }
    public int ReadinessScore { get; set; }
    public string Verdict { get; set; } = "";
    public string Reason { get; set; } = "";
    public decimal CompletionRate { get; set; }
    public bool IsProvisional { get; set; }
    public string SummaryJson { get; set; } = "{}";
    public string Model { get; set; } = "";
    public string PromptVersion { get; set; } = "rubric-v1";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class DeliveryMetric
{
    public Guid TurnId { get; set; }
    public int WordCount { get; set; }
    public double? WordsPerMinute { get; set; }
    public double? ThinkingSeconds { get; set; }
    public double? SpeakingSeconds { get; set; }
    public int? LongPauseCount { get; set; }
    public int? FillerCount { get; set; }
    public string FillerDetailsJson { get; set; } = "{}";
    public string Availability { get; set; } = "text";
}
public class AiRequestLog
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public Guid? SessionId { get; set; }
    public string Provider { get; set; } = "Gemini";
    public string KeySource { get; set; } = "Legacy";
    public DateTimeOffset? DispatchedAt { get; set; }
    public string Model { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Status { get; set; } = "";
    public int AttemptNumber { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? ThinkingTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class SkillTag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
public class QuestionBank
{
    public int Id { get; set; }
    public int PositionId { get; set; }
    public Position Position { get; set; } = null!;
    public string Content { get; set; } = "";
    public InterviewLevel Level { get; set; }
    public InterviewLanguage Language { get; set; }
    public InterviewKind Kind { get; set; }
    public string CompanyType { get; set; } = "Product";
    public bool IsActive { get; set; } = true;
    public List<QuestionSkillTag> Tags { get; set; } = [];
}
public class QuestionSkillTag
{
    public int QuestionBankId { get; set; }
    public QuestionBank Question { get; set; } = null!;
    public int SkillTagId { get; set; }
    public SkillTag Skill { get; set; } = null!;
}
public class Classroom
{
    public int Id { get; set; }
    public string LecturerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string JoinCode { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ClassMember> Members { get; set; } = [];
    public List<Assignment> Assignments { get; set; } = [];
}
public class ClassMember
{
    public int ClassId { get; set; }
    public Classroom Class { get; set; } = null!;
    public string StudentId { get; set; } = "";
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
/// <summary>A student the lecturer removed from a class; blocks rejoining with the join code.</summary>
public class ClassBan
{
    public int ClassId { get; set; }
    public string StudentId { get; set; } = "";
    public DateTimeOffset BannedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class Assignment
{
    public int Id { get; set; }
    public int ClassId { get; set; }
    public Classroom Class { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Instructions { get; set; } = "";
    public DateTimeOffset DueAt { get; set; }
    public int PositionId { get; set; }
    public Position Position { get; set; } = null!;
    public InterviewLevel Level { get; set; }
    public InterviewLanguage Language { get; set; }
    public InterviewKind Kind { get; set; }
    public int QuestionCount { get; set; } = 5;
    public int RubricVersionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<AssignmentSubmission> Submissions { get; set; } = [];
}
public class AssignmentSubmission
{
    public int Id { get; set; }
    public int AssignmentId { get; set; }
    public Assignment Assignment { get; set; } = null!;
    public string StudentId { get; set; } = "";
    public Guid SessionId { get; set; }
    public InterviewSession Session { get; set; } = null!;
    public int AttemptNumber { get; set; } = 1;
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsLate { get; set; }
}
public class LecturerComment
{
    public int Id { get; set; }
    public int ClassId { get; set; }
    public string LecturerId { get; set; } = "";
    public Guid SessionId { get; set; }
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
