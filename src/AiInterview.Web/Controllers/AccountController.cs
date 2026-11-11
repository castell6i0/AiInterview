using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.Identity;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.ViewModels;
using AiInterview.Web.Services;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace AiInterview.Web.Controllers;
public sealed class AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    AppDbContext db, AccountEmailService email) : Controller
{
    [AllowAnonymous] public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Home");
        return View(new LoginModel { ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
    }
    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Login(LoginModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await users.FindByEmailAsync(model.Email.Trim());
        var result = user is null ? Microsoft.AspNetCore.Identity.SignInResult.Failed : await signIn.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.IsLockedOut
                ? "Tài khoản đang tạm khóa. Vui lòng thử lại sau hoặc liên hệ quản trị viên."
                : "Chưa thể đăng nhập. Kiểm tra email, mật khẩu và xác nhận email nếu bạn vừa đăng ký.");
            return View(model);
        }
        var notification = await email.SendLoginNotificationAsync(user!, HttpContext.RequestAborted);
        if (notification != AccountMailResult.Sent)
            TempData["Message"] = "Bạn đã đăng nhập thành công. Email thông báo chưa gửi được; bạn vẫn có thể sử dụng ứng dụng.";
        if (Url.IsLocalUrl(model.ReturnUrl)) return LocalRedirect(model.ReturnUrl!);
        if (await users.IsInRoleAsync(user!, Roles.Admin)) return RedirectToAction("Index", "Admin");
        if (await users.IsInRoleAsync(user!, Roles.Lecturer)) return RedirectToAction("Index", "Lecturer");
        return RedirectToAction("Index", "Dashboard");
    }
    [AllowAnonymous] public IActionResult Register()
    {
        ViewData["EmailConfigured"] = email.IsConfigured;
        return View(new RegisterModel());
    }
    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Register(RegisterModel model)
    {
        ViewData["EmailConfigured"] = email.IsConfigured;
        if (!ModelState.IsValid) return View(model);
        // Email is optional for self-registration when the operator has not configured SMTP.
        // Do not claim ownership of an email address that has never been verified.
        var requiresConfirmation = email.IsConfigured;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new ApplicationUser { UserName = model.Email.Trim(), Email = model.Email.Trim(), FullName = model.FullName.Trim(), EmailConfirmationRequired = requiresConfirmation };
        var result = await users.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            if (result.Errors.All(error => error.Code.Contains("Duplicate", StringComparison.Ordinal)))
                return RegistrationAccepted(model.Email.Trim(), requiresConfirmation);
            foreach (var error in result.Errors)
                ModelState.AddModelError("", "Chưa thể tạo tài khoản với thông tin này. Kiểm tra định dạng email và mật khẩu (ít nhất 10 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt).");
            return View(model);
        }
        var roleResult = await users.AddToRoleAsync(user, Roles.Student);
        if (!roleResult.Succeeded) { ModelState.AddModelError("", "Chưa thể hoàn tất đăng ký. Vui lòng thử lại sau hoặc liên hệ quản trị viên nếu lỗi tiếp tục."); return View(model); }
        db.StudentProfiles.Add(new StudentProfile { UserId = user.Id, FullName = user.FullName });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        // Account data is committed before contacting SMTP. Delivery failure can be retried.
        if (requiresConfirmation)
        {
            await email.SendConfirmationAsync(user, HttpContext.RequestAborted);
        }
        return RegistrationAccepted(user.Email!, requiresConfirmation);
    }

    private IActionResult RegistrationAccepted(string address, bool requiresConfirmation)
    {
        // Same completion response for a newly created address and an already registered address.
        if (requiresConfirmation)
        {
            SetConfirmationMessage(address, AccountMailResult.NotNeeded);
            return RedirectToAction(nameof(CheckEmail));
        }
        TempData["Message"] = "Yêu cầu đăng ký đã được tiếp nhận. Bạn có thể đăng nhập bằng thông tin tài khoản của mình. Website chưa bật xác minh email; email mới chưa được xác minh nên chỉ dùng được key AI cá nhân.";
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult CheckEmail()
    {
        ViewData["EmailConfigured"] = email.IsConfigured;
        return View(new ConfirmationEmailModel
        {
            Email = TempData["ConfirmationEmail"] as string ?? "",
            Message = TempData["ConfirmationMessage"] as string,
            DeliveryFailed = TempData["ConfirmationFailed"] is true
        });
    }

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> ResendConfirmation(ConfirmationEmailModel model)
    {
        // These fields are display-only; never reflect posted status messages.
        ViewData["EmailConfigured"] = email.IsConfigured;
        model.Message = null;
        model.DeliveryFailed = false;
        ModelState.Remove(nameof(model.Message));
        ModelState.Remove(nameof(model.DeliveryFailed));
        if (!ModelState.IsValid) return View("CheckEmail", model);
        var user = await users.FindByEmailAsync(model.Email.Trim());
        if (user is not null && !user.EmailConfirmed)
            await email.SendConfirmationAsync(user, HttpContext.RequestAborted);
        // The same response also covers pending accounts when SMTP is unavailable.
        SetConfirmationMessage(model.Email.Trim(), AccountMailResult.NotNeeded);
        return RedirectToAction(nameof(CheckEmail));
    }

    [HttpGet, AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ConfirmEmail(string? userId, string? code)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var model = new ConfirmEmailResultModel();
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 450 || string.IsNullOrWhiteSpace(code) || code.Length > 4096)
            return View(model);
        var user = await users.FindByIdAsync(userId);
        if (user is null) return View(model);
        try
        {
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            model.Succeeded = (await users.ConfirmEmailAsync(user, token)).Succeeded;
        }
        catch (FormatException) { /* Malformed links receive the same result as expired tokens. */ }
        return View(model);
    }

    private void SetConfirmationMessage(string address, AccountMailResult result)
    {
        TempData["ConfirmationEmail"] = address;
        TempData["ConfirmationFailed"] = result is AccountMailResult.Failed or AccountMailResult.NotConfigured;
        TempData["ConfirmationMessage"] = result switch
        {
            AccountMailResult.Failed => "Chưa gửi được email lúc này. Tài khoản đã được lưu nếu bạn vừa đăng ký. Chờ một phút rồi gửi lại email xác nhận.",
            AccountMailResult.NotConfigured => "Chức năng gửi email chưa sẵn sàng. Vui lòng liên hệ quản trị viên hoặc quay lại sau.",
            AccountMailResult.Sent => "Đã gửi email xác nhận. Mở hộp thư của bạn và nhấn vào liên kết để hoàn tất đăng ký. Hãy kiểm tra cả thư rác.",
            _ => "Yêu cầu đã được tiếp nhận. Nếu email này có tài khoản chưa xác nhận, hãy kiểm tra hộp thư đến và thư rác. Chưa thấy thư? Chờ ít nhất một phút rồi gửi lại hoặc liên hệ quản trị viên."
        };
    }
    [HttpPost, Authorize] public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return RedirectToAction("Index", "Home"); }
    public IActionResult AccessDenied() { Response.StatusCode = 403; return View(); }
}
