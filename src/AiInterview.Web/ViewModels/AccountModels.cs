using System.ComponentModel.DataAnnotations;
namespace AiInterview.Web.ViewModels;
public sealed class LoginModel
{
    [Required(ErrorMessage = "Nhập email."), EmailAddress(ErrorMessage = "Email chưa hợp lệ.")] public string Email { get; set; } = "";
    [Required(ErrorMessage = "Nhập mật khẩu."), DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}
public sealed class RegisterModel
{
    [Required(ErrorMessage = "Nhập họ tên."), StringLength(120)] public string FullName { get; set; } = "";
    [Required(ErrorMessage = "Nhập email."), EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, MinLength(10, ErrorMessage = "Mật khẩu cần ít nhất 10 ký tự."), MaxLength(100), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận chưa khớp."), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
}

public sealed class ConfirmationEmailModel
{
    [Required(ErrorMessage = "Nhập email đã đăng ký."), EmailAddress(ErrorMessage = "Email chưa hợp lệ."), StringLength(256)]
    public string Email { get; set; } = "";
    public string? Message { get; set; }
    public bool DeliveryFailed { get; set; }
}

public sealed class ConfirmEmailResultModel
{
    public bool Succeeded { get; set; }
}
