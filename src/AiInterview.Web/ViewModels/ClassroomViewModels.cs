using System.ComponentModel.DataAnnotations;
using AiInterview.Domain.Entities;

namespace AiInterview.Web.ViewModels;

public sealed class CreateClassVm
{
    [Required(ErrorMessage = "Nhập tên lớp."), StringLength(150)]
    [Display(Name = "Tên lớp")] public string Name { get; set; } = "";
}
public sealed class JoinClassVm
{
    [Required(ErrorMessage = "Nhập mã lớp."), StringLength(16)]
    [Display(Name = "Mã lớp")] public string Code { get; set; } = "";
    [Range(typeof(bool), "true", "true", ErrorMessage = "Vui lòng xác nhận phạm vi chia sẻ với giảng viên.")]
    public bool AcceptSharing { get; set; }
}
public sealed class AssignmentEditVm
{
    public int ClassId { get; set; }
    [Required(ErrorMessage = "Chưa nhập tên bài tập."), StringLength(160, ErrorMessage = "Tên bài tập tối đa 160 ký tự."), Display(Name = "Tên bài tập")] public string Title { get; set; } = "";
    [Required(ErrorMessage = "Chưa nhập hướng dẫn cho sinh viên."), StringLength(4000, ErrorMessage = "Hướng dẫn tối đa 4.000 ký tự."), Display(Name = "Hướng dẫn")] public string Instructions { get; set; } = "";
    [DataType(DataType.DateTime), Display(Name = "Hạn nộp (giờ Việt Nam, UTC+7)")] public DateTime DueAtVietnam { get; set; } = DateTime.UtcNow.AddDays(7).AddHours(7);
    [Range(1, int.MaxValue, ErrorMessage = "Chưa chọn vị trí phỏng vấn."), Display(Name = "Vị trí")] public int PositionId { get; set; }
    [EnumDataType(typeof(InterviewLevel))] public InterviewLevel Level { get; set; }
    [EnumDataType(typeof(InterviewLanguage))] public InterviewLanguage Language { get; set; }
    [EnumDataType(typeof(InterviewKind))] public InterviewKind Kind { get; set; }
    [Range(5, 10, ErrorMessage = "Số câu chính phải từ 5 đến 10."), Display(Name = "Số câu chính")] public int QuestionCount { get; set; } = 5;
    public List<Position> Positions { get; set; } = [];
}
public sealed class LecturerCommentVm
{
    public int ClassId { get; set; }
    public Guid SessionId { get; set; }
    [Required, StringLength(3000), Display(Name = "Nhận xét")] public string Content { get; set; } = "";
}
public sealed class ClassListVm
{
    public List<Classroom> Classes { get; set; } = [];
    public JoinClassVm Join { get; set; } = new();
}
public sealed class StudentClassVm
{
    public Classroom Class { get; set; } = null!;
    public List<Assignment> Assignments { get; set; } = [];
    public List<AssignmentSubmission> Submissions { get; set; } = [];
}
public sealed class AssignmentDetailVm
{
    public Assignment Assignment { get; set; } = null!;
    public List<InterviewSession> EligibleSessions { get; set; } = [];
    public List<AssignmentSubmission> Submissions { get; set; } = [];
}
public sealed class StudentSummaryVm
{
    public string StudentId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LastPracticeAt { get; set; }
    public int CompletedCount { get; set; }
    public decimal? AverageScore { get; set; }
    public bool Inactive { get; set; }
    public bool RepeatedLowScores { get; set; }
}
public sealed class SkillAverageVm
{
    public string Skill { get; set; } = "";
    public decimal Score { get; set; }
    public int SampleCount { get; set; }
}
public sealed class LecturerClassVm
{
    public Classroom Class { get; set; } = null!;
    public List<StudentSummaryVm> Students { get; set; } = [];
    public List<SkillAverageVm> Skills { get; set; } = [];
    public List<Assignment> Assignments { get; set; } = [];
    public List<AssignmentSubmission> Submissions { get; set; } = [];
    public decimal? AverageScore { get; set; }
    public int CompletedCount { get; set; }
}
public sealed class LecturerStudentVm
{
    public Classroom Class { get; set; } = null!;
    public StudentSummaryVm Student { get; set; } = null!;
    public List<InterviewSession> Sessions { get; set; } = [];
    public List<LecturerComment> Comments { get; set; } = [];
}
