namespace AiInterview.Application.Interfaces;

public interface IAccountEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string recipient, string subject, string htmlBody, string textBody, CancellationToken cancellationToken = default);
}

public sealed class AccountEmailException(string code) : Exception("Chưa gửi được email. Vui lòng thử lại sau.")
{
    public string Code { get; } = code;
}
