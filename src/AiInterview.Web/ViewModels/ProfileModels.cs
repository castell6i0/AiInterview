using System.ComponentModel.DataAnnotations;
using AiInterview.Application.DTOs;
using AiInterview.Domain.Entities;

namespace AiInterview.Web.ViewModels;

public sealed class EditProfileModel
{
    [Required(ErrorMessage = "Nhập họ tên."), StringLength(160)]
    [Display(Name = "Họ và tên")] public string FullName { get; set; } = "";
    [StringLength(250), Display(Name = "Trường")] public string? University { get; set; }
    [StringLength(160), Display(Name = "Chuyên ngành")] public string? Major { get; set; }
    [Range(1, 8, ErrorMessage = "Năm học từ 1 đến 8."), Display(Name = "Năm học")] public int AcademicYear { get; set; } = 4;
}
public sealed class ProfileIndexModel
{
    public EditProfileModel Profile { get; set; } = new();
    public List<CvProfile> Cvs { get; set; } = [];
    public List<JobDescription> Jds { get; set; } = [];
}
public sealed class AiConsentModel
{
    [Required(ErrorMessage = "Hãy tải lại trang và xác nhận nhà cung cấp AI trước khi tiếp tục.")]
    [RegularExpression("^(Gemini|OpenAI)$", ErrorMessage = "Hãy tải lại trang và xác nhận nhà cung cấp AI trước khi tiếp tục.")]
    public string ConsentedProvider { get; set; } = "";
    [Range(typeof(bool), "true", "true", ErrorMessage = "Hãy đồng ý gửi nội dung đã chọn tới nhà cung cấp AI đã chọn để phân tích.")]
    public bool Consent { get; set; }
}
public sealed class CvDetailModel
{
    public CvProfile Cv { get; set; } = null!;
    public CvAnalysis? Analysis { get; set; }
    public bool CanDelete { get; set; }
    public bool AiConfigured { get; set; }
}
public sealed class CreateJdModel
{
    [Required(ErrorMessage = "Nhập tiêu đề tin tuyển dụng."), StringLength(200), Display(Name = "Tiêu đề tin tuyển dụng")]
    public string Title { get; set; } = "";
    [StringLength(160), Display(Name = "Công ty")] public string? Company { get; set; }
    [Required(ErrorMessage = "Dán nội dung mô tả công việc."), StringLength(30000, MinimumLength = 30, ErrorMessage = "JD cần từ 30 đến 30.000 ký tự."), Display(Name = "Nội dung JD")]
    public string Content { get; set; } = "";
    [StringLength(2048), Url(ErrorMessage = "Nhập URL hợp lệ, bắt đầu bằng https:// hoặc http://."), Display(Name = "Link tin tuyển dụng (tùy chọn)")]
    public string? SourceUrl { get; set; }
}
public sealed class CompareGapModel
{
    [Required(ErrorMessage = "Hãy tải lại trang và xác nhận nhà cung cấp AI trước khi tiếp tục.")]
    [RegularExpression("^(Gemini|OpenAI)$", ErrorMessage = "Hãy tải lại trang và xác nhận nhà cung cấp AI trước khi tiếp tục.")]
    public string ConsentedProvider { get; set; } = "";
    public int JdId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Chọn một CV để so sánh.")] public int CvId { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Hãy đồng ý gửi CV và JD đã chọn tới nhà cung cấp AI đã chọn.")] public bool Consent { get; set; }
}
public sealed class JdDetailModel
{
    public JobDescription Jd { get; set; } = null!;
    public SkillGapAnalysis? Gap { get; set; }
    public List<CvProfile> Cvs { get; set; } = [];
    public bool CanDelete { get; set; }
    public bool AiConfigured { get; set; }
}
