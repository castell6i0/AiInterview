using AiInterview.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AiInterview.Web.ViewModels;

public static class ClassroomUi
{
    public static IEnumerable<SelectListItem> Options<T>() where T : struct, Enum => Enum.GetValues<T>().Select(value =>
        new SelectListItem(Label(value), Convert.ToInt32(value).ToString()));
    public static string Label(Enum value) => value switch
    {
        InterviewLevel.Intern => "Thực tập sinh", InterviewLevel.Fresher => "Mới tốt nghiệp",
        InterviewLanguage.Vietnamese => "Tiếng Việt", InterviewLanguage.English => "Tiếng Anh",
        InterviewKind.Technical => "Kỹ thuật", InterviewKind.Behavioral => "Hành vi / HR", InterviewKind.Project => "Dự án trong CV",
        InterviewKind.JobDescription => "Bám theo JD", InterviewKind.Mixed => "Tổng hợp", InterviewKind.Coding => "Câu hỏi code",
        SessionStatus.Created => "Chưa bắt đầu", SessionStatus.InProgress => "Đang phỏng vấn", SessionStatus.AwaitingQuestion => "Đang chờ câu hỏi",
        SessionStatus.ReadyForEvaluation => "Chờ chấm điểm", SessionStatus.Completed => "Đã chấm điểm",
        _ => value.ToString()
    };
    public static string Date(DateTimeOffset? value) => value?.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm") ?? "Chưa có";
}
