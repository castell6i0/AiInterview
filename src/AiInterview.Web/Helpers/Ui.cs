using AiInterview.Domain.Entities;
namespace AiInterview.Web.Helpers;
public static class Ui
{
    public static string Label(object? value) => value switch
    {
        InterviewLanguage.Vietnamese => "Tiếng Việt", InterviewLanguage.English => "Tiếng Anh",
        InterviewMode.Text => "Văn bản", InterviewMode.Voice => "Giọng nói",
        InterviewKind.Technical => "Kỹ thuật", InterviewKind.Behavioral => "Hành vi / HR", InterviewKind.Project => "Dự án trong CV",
        InterviewKind.JobDescription => "Bám sát JD", InterviewKind.Mixed => "Tổng hợp", InterviewKind.Coding => "Bài tập code",
        InterviewPersona.Friendly => "HR thân thiện", InterviewPersona.Strict => "Tech lead nghiêm", InterviewPersona.Challenger => "Phản biện / áp lực",
        SessionStatus.Created => "Chưa bắt đầu", SessionStatus.InProgress => "Đang phỏng vấn", SessionStatus.AwaitingQuestion => "Chờ câu tiếp",
        SessionStatus.ReadyForEvaluation => "Chờ chấm điểm", SessionStatus.Completed => "Đã có báo cáo", _ => value?.ToString() ?? "—"
    };
    public static string Time(DateTimeOffset? value) => value?.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm") ?? "—";
    public static string[] Strings(string? value) => AiInterview.Application.Services.AiPrompts.Read<string[]>(value) ?? [];
    // One score band for every score on screen (0–10; pass readiness 0–100 as value / 10). CSS: .score-{Band}.
    public static (string Band, string Label) Score(decimal? score) => score switch
    {
        null => ("none", "Chưa có điểm"),
        < 5m => ("low", "Cần cải thiện"),
        < 7m => ("mid", "Khá"),
        < 8.5m => ("good", "Tốt"),
        _ => ("top", "Xuất sắc")
    };
}
