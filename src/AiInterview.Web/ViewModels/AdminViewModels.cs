using System.ComponentModel.DataAnnotations;
using AiInterview.Domain.Entities;

namespace AiInterview.Web.ViewModels;

public sealed class AdminIndexVm
{
    public int UserCount { get; set; }
    public int SessionCount { get; set; }
    public int QuestionCount { get; set; }
    public long ReportedInputTokens { get; set; }
    public long ReportedOutputTokens { get; set; }
    public decimal KnownEstimatedCostUsd { get; set; }
    public int UnknownCostCount { get; set; }
    public int AiRequestCount { get; set; }
    public List<string> DayLabels { get; set; } = [];
    public List<int> SessionsPerDay { get; set; } = [];
    public List<int> AiCallsPerDay { get; set; } = [];
    /// <summary>Student, lecturer, admin counts in that order.</summary>
    public List<int> UsersByRole { get; set; } = [];
    /// <summary>Full evaluations per score band: low, mid, good, top.</summary>
    public List<int> ScoreBands { get; set; } = [];
}
public sealed class AdminUserVm
{
    public string Id { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public List<string> Roles { get; set; } = [];
    public bool Locked { get; set; }
}
public sealed class AdminUsersVm
{
    public string Search { get; set; } = "";
    public List<AdminUserVm> Users { get; set; } = [];
    public bool HasMore { get; set; }
}
public sealed class UserAccessVm
{
    [Required] public string UserId { get; set; } = "";
    [Required] public string Role { get; set; } = "Student";
    public bool Locked { get; set; }
}
public sealed class CreateUserVm
{
    [Required(ErrorMessage = "Nhập họ tên."), StringLength(160), Display(Name = "Họ tên")]
    public string FullName { get; set; } = "";
    [Required(ErrorMessage = "Nhập email."), EmailAddress(ErrorMessage = "Email chưa hợp lệ."), StringLength(256), Display(Name = "Email")]
    public string Email { get; set; } = "";
    [Required(ErrorMessage = "Nhập mật khẩu."), MinLength(10, ErrorMessage = "Mật khẩu cần ít nhất 10 ký tự."), MaxLength(100), DataType(DataType.Password), Display(Name = "Mật khẩu")]
    public string Password { get; set; } = "";
    [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận chưa khớp."), DataType(DataType.Password), Display(Name = "Nhập lại mật khẩu")]
    public string ConfirmPassword { get; set; } = "";
    [Required(ErrorMessage = "Chọn vai trò."), Display(Name = "Vai trò")]
    public string Role { get; set; } = Roles.Student;
}
public sealed class EditUserVm
{
    [Required] public string UserId { get; set; } = "";
    [Required(ErrorMessage = "Nhập họ tên."), StringLength(160), Display(Name = "Họ tên")]
    public string FullName { get; set; } = "";
    [Required(ErrorMessage = "Nhập email."), EmailAddress(ErrorMessage = "Email chưa hợp lệ."), StringLength(256), Display(Name = "Email")]
    public string Email { get; set; } = "";
}
public sealed class DeleteUserVm
{
    public string UserId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool CanDelete { get; set; }
    public string RefusalReason { get; set; } = "";
}
public sealed class PositionEditVm
{
    public int Id { get; set; }
    [Required, StringLength(100), Display(Name = "Tên vị trí")] public string Name { get; set; } = "";
    [StringLength(1500), Display(Name = "Mô tả")] public string? Description { get; set; }
    [Display(Name = "Đang hoạt động")] public bool IsActive { get; set; } = true;
}
public sealed class QuestionEditVm
{
    public int Id { get; set; }
    [Range(1, int.MaxValue), Display(Name = "Vị trí")] public int PositionId { get; set; }
    [Required, StringLength(4000), Display(Name = "Câu hỏi")] public string Content { get; set; } = "";
    [EnumDataType(typeof(InterviewLevel))] public InterviewLevel Level { get; set; }
    [EnumDataType(typeof(InterviewLanguage))] public InterviewLanguage Language { get; set; }
    [EnumDataType(typeof(InterviewKind))] public InterviewKind Kind { get; set; }
    [Required, StringLength(60), Display(Name = "Loại công ty")] public string CompanyType { get; set; } = "Product";
    [StringLength(500), Display(Name = "Chủ đề kỹ năng (ngăn cách bằng dấu phẩy)")] public string? SkillTags { get; set; }
    [Display(Name = "Đang hoạt động")] public bool IsActive { get; set; } = true;
    public List<Position> Positions { get; set; } = [];
}
public sealed class AdminQuestionsVm
{
    public string Search { get; set; } = "";
    public int? PositionId { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public List<Position> Positions { get; set; } = [];
    public List<QuestionBank> Questions { get; set; } = [];
}
public sealed class RubricEditVm
{
    [Required, StringLength(150), Display(Name = "Tên phiên bản")] public string Name { get; set; } = "";
    public List<RubricCriterionEditVm> Criteria { get; set; } = [];
    public List<RubricVersion> History { get; set; } = [];
}
public sealed class RubricCriterionEditVm
{
    [Required, StringLength(40)] public string Code { get; set; } = "";
    [Required, StringLength(150), Display(Name = "Tên tiêu chí")] public string Name { get; set; } = "";
    [Required, StringLength(2500), Display(Name = "Rubric chấm điểm")] public string Description { get; set; } = "";
    [Microsoft.AspNetCore.Mvc.ModelBinder(BinderType = typeof(RubricWeightModelBinder))]
    [Range(0.01, 100), Display(Name = "Trọng số")] public decimal Weight { get; set; }
}
