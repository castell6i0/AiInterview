using System.ComponentModel.DataAnnotations;
using AiInterview.Domain.Entities;

namespace AiInterview.Web.ViewModels;

public sealed record ProgressPoint(Guid Id, string Position, string Date, decimal Score, double? WordsPerMinute, double? ThinkingSeconds, double? FillerCount, double? LongPauseCount, double? WordCount);
public sealed record SkillProgress(string Skill, decimal Score, int AnswerCount);
public sealed record QuestionRecommendation(int Id, string Content, string Position, InterviewLevel Level, InterviewLanguage Language, InterviewKind Kind, string CompanyType, string[] Skills);
public sealed class DashboardModel
{
    public int? PositionId { get; set; }
    public InterviewLanguage? Language { get; set; }
    public InterviewLevel? Level { get; set; }
    public List<Position> Positions { get; set; } = [];
    public bool HasAnySessions { get; set; }
    public int TotalSessions { get; set; }
    public int CompletedSessions { get; set; }
    public int FullEvaluatedSessions { get; set; }
    public decimal? AverageScore { get; set; }
    public List<InterviewSession> Recent { get; set; } = [];
    public List<ProgressPoint> Progress { get; set; } = [];
    public List<SkillProgress> Skills { get; set; } = [];
    public List<QuestionRecommendation> Recommendations { get; set; } = [];
    public PracticeWeakModel Practice { get; set; } = new();
}
public sealed class PracticeWeakModel
{
    [Required(ErrorMessage = "Hãy tải lại trang và xác nhận nhà cung cấp AI trước khi tiếp tục.")]
    [RegularExpression("^(Gemini|OpenAI)$", ErrorMessage = "Hãy tải lại trang và xác nhận nhà cung cấp AI trước khi tiếp tục.")]
    public string ConsentedProvider { get; set; } = "";
    [Range(1, int.MaxValue)] public int PositionId { get; set; }
    [EnumDataType(typeof(InterviewLanguage))] public InterviewLanguage Language { get; set; }
    [EnumDataType(typeof(InterviewLevel))] public InterviewLevel Level { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Hãy đồng ý gửi câu trả lời tới nhà cung cấp AI đã chọn để luyện tập và chấm điểm.")]
    public bool Consent { get; set; }
}
public sealed class HistoryModel
{
    public int? PositionId { get; set; }
    public InterviewLanguage? Language { get; set; }
    public SessionStatus? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<Position> Positions { get; set; } = [];
    public List<InterviewSession> Sessions { get; set; } = [];
    public int Page { get; set; } = 1;
    public int PageCount { get; set; }
    public int TotalCount { get; set; }
}
