using System.ComponentModel.DataAnnotations;
using AiInterview.Application.DTOs;
using AiInterview.Domain.Entities;

namespace AiInterview.Web.ViewModels;

public sealed class CreateInterviewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn vị trí.")]
    public int PositionId { get; set; }
    public int? CvProfileId { get; set; }
    public int? JobDescriptionId { get; set; }
    public int? AssignmentId { get; set; }
    [EnumDataType(typeof(InterviewLevel))] public InterviewLevel Level { get; set; }
    [EnumDataType(typeof(InterviewLanguage))] public InterviewLanguage Language { get; set; }
    [EnumDataType(typeof(InterviewMode))] public InterviewMode Mode { get; set; }
    [EnumDataType(typeof(InterviewKind))] public InterviewKind Kind { get; set; }
    [EnumDataType(typeof(InterviewPersona))] public InterviewPersona Persona { get; set; }
    [Range(5, 10, ErrorMessage = "Chọn từ 5 đến 10 câu chính.")] public int QuestionCount { get; set; } = 5;
    [StringLength(500)] public string? FocusSkills { get; set; }
    public List<Position> Positions { get; set; } = [];
    public List<CvProfile> Cvs { get; set; } = [];
    public List<JobDescription> Jds { get; set; } = [];
    public string? AssignmentTitle { get; set; }
    public bool AiConfigured { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Vui lòng đồng ý gửi dữ liệu buổi luyện tới nhà cung cấp AI đã chọn.")]
    public bool ConsentToAi { get; set; }
    [Required(ErrorMessage = "Hãy tải lại trang và xác nhận nhà cung cấp AI trước khi tiếp tục.")]
    [RegularExpression("^(Gemini|OpenAI)$", ErrorMessage = "Hãy tải lại trang và xác nhận nhà cung cấp AI trước khi tiếp tục.")]
    public string ConsentedProvider { get; set; } = "";
}
public sealed class SubmitAnswerModel
{
    public Guid TurnId { get; set; }
    [StringLength(20000, ErrorMessage = "Câu trả lời tối đa 20.000 ký tự.")]
    public string Answer { get; set; } = "";
    public bool Skip { get; set; }
    public SpeechTelemetry? Telemetry { get; set; }
}
public sealed class DraftAnswerModel
{
    public Guid TurnId { get; set; }
    [StringLength(20000)] public string Answer { get; set; } = "";
}
public sealed class MarkTurnModel
{
    public Guid TurnId { get; set; }
    public bool Starred { get; set; }
    public bool NeedsPractice { get; set; }
}
public sealed class RoomModel
{
    public InterviewSession Session { get; set; } = null!;
    public bool AiConfigured { get; set; }
}
public sealed class ReportModel
{
    public InterviewSession Session { get; set; } = null!;
    public List<AiRequestLog> Usage { get; set; } = [];
    public List<LecturerComment> Comments { get; set; } = [];
}
public sealed class CompareModel
{
    public InterviewSession Left { get; set; } = null!;
    public InterviewSession Right { get; set; } = null!;
}
