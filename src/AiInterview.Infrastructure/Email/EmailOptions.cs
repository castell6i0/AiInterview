using System.Net.Mail;

namespace AiInterview.Infrastructure.Email;

public sealed class EmailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string Security { get; set; } = "StartTls";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "InterviewLab";
    public string PublicBaseUrl { get; set; } = "http://localhost:5088";

    public bool SmtpConfigured => Enabled && !string.IsNullOrWhiteSpace(Host)
        && !Host.Any(char.IsWhiteSpace) && Uri.CheckHostName(Host) != UriHostNameType.Unknown
        && Port is > 0 and <= 65535 && Security is "StartTls" or "SslOnConnect"
        && !string.IsNullOrWhiteSpace(Username) && !HasNewLine(Username)
        && !string.IsNullOrWhiteSpace(Password) && !HasNewLine(Password)
        && IsMailbox(FromAddress) && !HasInvalidHeader(FromName) && FromName.Length <= 120;

    public bool HasValidPublicUrl => !string.IsNullOrWhiteSpace(PublicBaseUrl)
        && !PublicBaseUrl.Any(char.IsWhiteSpace) && !PublicBaseUrl.Any(char.IsControl)
        && Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/";

    public static bool HasNewLine(string? value) => value is null || value.Contains('\r') || value.Contains('\n');
    public static bool HasInvalidHeader(string? value) => value is null || value.Any(char.IsControl);
    public static bool IsMailbox(string? value) => !string.IsNullOrWhiteSpace(value) && !HasInvalidHeader(value)
        && value.Length <= 256 && MailAddress.TryCreate(value, out var parsed)
        && parsed.Address.Equals(value, StringComparison.OrdinalIgnoreCase);
}
