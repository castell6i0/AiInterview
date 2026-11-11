using System.Text;
using System.Text.Encodings.Web;
using AiInterview.Application.Interfaces;
using AiInterview.Infrastructure.Email;
using AiInterview.Infrastructure.Identity;
using AiInterview.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiInterview.Web.Services;

public enum AccountMailResult { Sent, NotConfigured, Failed, Throttled, NotNeeded }

public sealed class AccountEmailService(UserManager<ApplicationUser> users, AppDbContext db,
    IAccountEmailSender sender, IOptions<EmailOptions> settings, TimeProvider clock,
    IOptions<RequestProtectionOptions>? protection = null)
{
    public bool IsConfigured => sender.IsConfigured && settings.Value.HasValidPublicUrl;

    public async Task<AccountMailResult> SendConfirmationAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (user.EmailConfirmed) return AccountMailResult.NotNeeded;
        if (!IsConfigured) return AccountMailResult.NotConfigured;
        var now = clock.GetUtcNow();
        var cooldown = (protection?.Value ?? new RequestProtectionOptions()).EmailAccountCooldownSeconds;
        // An atomic SQL reservation also throttles concurrent resend requests from different IPs.
        var reserved = await db.Users.Where(x => x.Id == user.Id && !x.EmailConfirmed
                && (x.ConfirmationEmailRequestedAt == null || x.ConfirmationEmailRequestedAt <= now.AddSeconds(-cooldown)))
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ConfirmationEmailRequestedAt, now), ct);
        if (reserved == 0) return AccountMailResult.Throttled;
        user.ConfirmationEmailRequestedAt = now;
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var origin = new Uri(settings.Value.PublicBaseUrl).GetLeftPart(UriPartial.Authority);
        var url = QueryHelpers.AddQueryString(origin + "/Account/ConfirmEmail",
            new Dictionary<string, string?> { ["userId"] = user.Id, ["code"] = code });
        var name = HtmlEncoder.Default.Encode(user.FullName);
        var safeUrl = HtmlEncoder.Default.Encode(url);
        var text = $"Chào {user.FullName},\nXác nhận email để hoàn tất đăng ký InterviewLab:\n{url}\nLiên kết có hiệu lực 24 giờ. Nếu bạn không đăng ký, hãy bỏ qua email này.";
        var html = Frame("Xác nhận email của bạn", $"<p>Chào {name},</p><p>Còn một bước để bắt đầu luyện phỏng vấn: xác nhận đây là email của bạn.</p><p style=\"margin:28px 0\"><a href=\"{safeUrl}\" style=\"background:#254e40;color:#fff;padding:14px 22px;border-radius:8px;text-decoration:none;display:inline-block\">Xác nhận email</a></p><p>Liên kết có hiệu lực <strong>24 giờ</strong>.</p><p>Nếu nút không mở được, sao chép liên kết này vào trình duyệt:</p><p style=\"word-break:break-all\">{safeUrl}</p><p>Nếu bạn không đăng ký, hãy bỏ qua email này.</p>");
        return await SendAsync(user.Email!, "InterviewLab · Xác nhận đăng ký", html, text, ct);
    }

    public async Task<AccountMailResult> SendLoginNotificationAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (!IsConfigured) return AccountMailResult.NotConfigured;
        var time = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm:ss, dd/MM/yyyy");
        var text = $"Chào {user.FullName},\nTài khoản InterviewLab của bạn vừa đăng nhập thành công lúc {time} (UTC+7).\nNếu đây là bạn, không cần làm gì thêm. Nếu không phải bạn, hãy liên hệ quản trị viên để khóa tài khoản và kiểm tra.";
        var html = Frame("Đăng nhập thành công", $"<p>Chào {HtmlEncoder.Default.Encode(user.FullName)},</p><p>Tài khoản InterviewLab của bạn vừa đăng nhập thành công.</p><p><strong>Thời gian:</strong> {time} (UTC+7)</p><p>Nếu đây là bạn, không cần làm gì thêm.</p><p>Nếu không phải bạn, hãy liên hệ quản trị viên để khóa tài khoản và kiểm tra.</p>");
        return await SendAsync(user.Email!, "InterviewLab · Đăng nhập thành công", html, text, ct);
    }

    private async Task<AccountMailResult> SendAsync(string email, string subject, string html, string text, CancellationToken ct)
    {
        try { await sender.SendAsync(email, subject, html, text, ct); return AccountMailResult.Sent; }
        catch (AccountEmailException) { return AccountMailResult.Failed; }
    }

    private static string Frame(string title, string content) => $"<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><body style=\"margin:0;background:#f5f7f2;font-family:Arial,sans-serif;color:#202d29;line-height:1.7\"><div style=\"max-width:580px;margin:32px auto;background:white;padding:32px;border:1px solid #dce4d8;border-radius:14px\"><strong style=\"color:#254e40;font-size:22px\">InterviewLab</strong><h1 style=\"font-size:24px;line-height:1.4\">{title}</h1>{content}<hr style=\"border:0;border-top:1px solid #dce4d8;margin:28px 0\"><p style=\"font-size:13px;color:#586b5d\">Email tự động từ InterviewLab. Không trả lời email này.</p></div></body></html>";
}
