using System.Net.Sockets;
using AiInterview.Application.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AiInterview.Infrastructure.Email;

public sealed class SmtpAccountEmailSender : IAccountEmailSender
{
    private readonly DeliverySettings? _settings;
    private readonly ILogger<SmtpAccountEmailSender> _logger;
    private readonly Func<SmtpClient> _createClient;

    public SmtpAccountEmailSender(IOptions<EmailOptions> settings, ILogger<SmtpAccountEmailSender> logger)
        : this(settings, logger, () => new SmtpClient()) { }

    internal SmtpAccountEmailSender(IOptions<EmailOptions> settings, ILogger<SmtpAccountEmailSender> logger,
        Func<SmtpClient> createClient)
    {
        // Copy the bound configuration once; do not reread mutable options during delivery.
        var source = settings.Value;
        var snapshot = new EmailOptions
        {
            Enabled = source.Enabled, Host = source.Host, Port = source.Port, Security = source.Security,
            Username = source.Username, Password = source.Password,
            FromAddress = source.FromAddress, FromName = source.FromName
        };
        _settings = snapshot.SmtpConfigured ? new DeliverySettings(snapshot.Host, snapshot.Port,
            snapshot.Security == "SslOnConnect" ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
            snapshot.Username, snapshot.Password, snapshot.FromAddress, snapshot.FromName) : null;
        _logger = logger;
        _createClient = createClient;
    }

    public bool IsConfigured => _settings is not null;

    public async Task SendAsync(string recipient, string subject, string htmlBody, string textBody, CancellationToken cancellationToken = default)
    {
        var options = _settings ?? throw new AccountEmailException("not_configured");
        if (!EmailOptions.IsMailbox(recipient) || string.IsNullOrWhiteSpace(subject)
            || EmailOptions.HasInvalidHeader(subject)
            || string.IsNullOrWhiteSpace(htmlBody) && string.IsNullOrWhiteSpace(textBody))
            throw new AccountEmailException("invalid_message");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var client = _createClient();
        client.Timeout = 15000;
        try
        {
            using var message = new MimeMessage();
            message.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
            message.To.Add(MailboxAddress.Parse(recipient));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody }.ToMessageBody();
            // STARTTLS is required, not opportunistic. Keep MailKit's certificate validation.
            await client.ConnectAsync(options.Host, options.Port, options.Security, timeout.Token);
            await client.AuthenticateAsync(options.Username, options.Password, timeout.Token);
            await client.SendAsync(message, timeout.Token);
            // DATA was accepted. A failed QUIT must not turn it into a failed send/duplicate retry.
            try { await client.DisconnectAsync(true, timeout.Token); }
            catch (Exception ex) when (IsDeliveryFailure(ex)) { }
        }
        catch (Exception ex) when (IsDeliveryFailure(ex))
        {
            var code = ex is MailKit.Security.AuthenticationException ? "authentication" : ex is OperationCanceledException ? "timeout" : "delivery_failed";
            // Provider exceptions can contain credentials, addresses or message content: log only a fixed code.
            _logger.LogWarning("Email delivery failed: {Code}", code);
            throw new AccountEmailException(code);
        }
    }

    private static bool IsDeliveryFailure(Exception ex) => ex is MailKit.Security.AuthenticationException
        or MailKit.ServiceNotAuthenticatedException or MailKit.ServiceNotConnectedException
        or SmtpCommandException or SmtpProtocolException or SslHandshakeException or SocketException
        or IOException or OperationCanceledException or NotSupportedException;

    private sealed record DeliverySettings(string Host, int Port, SecureSocketOptions Security,
        string Username, string Password, string FromAddress, string FromName);
}
