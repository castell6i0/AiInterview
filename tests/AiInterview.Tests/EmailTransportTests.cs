using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AiInterview.Application.Interfaces;
using AiInterview.Infrastructure.Email;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Xunit;

namespace AiInterview.Tests;

public class EmailTransportTests
{
    private static EmailOptions Configured() => new()
    {
        Enabled = true, Host = "smtp.example.com", Username = "test-only-username",
        Password = "test-only-password", FromAddress = "sender@example.com"
    };

    [Fact]
    public void EmailIsDisabledByDefaultAndRequiresCompleteConfiguration()
    {
        Assert.False(new EmailOptions().SmtpConfigured);
        Assert.True(Configured().SmtpConfigured);
        var options = Configured();
        options.Enabled = false;
        Assert.False(options.SmtpConfigured);
    }

    [Theory]
    [InlineData("Host", "smtp.example.com\r\nAUTH PLAIN injected")]
    [InlineData("Host", "https://smtp.example.com")]
    [InlineData("Host", "smtp example.com")]
    [InlineData("Security", "None")]
    [InlineData("Security", "Auto")]
    [InlineData("Security", "StartTlsWhenAvailable")]
    [InlineData("Username", "")]
    [InlineData("Username", "user\r\nAUTH injected")]
    [InlineData("Password", "")]
    [InlineData("Password", "password\nINJECTED")]
    [InlineData("FromAddress", "Sender <sender@example.com>")]
    [InlineData("FromAddress", "sender@example.com,other@example.com")]
    [InlineData("FromAddress", "sender@example.com\r\nBcc: other@example.com")]
    [InlineData("FromName", "Sender\u0000injected")]
    [InlineData("FromName", "Sender\r\nBcc: other@example.com")]
    public void InvalidSmtpConfigurationIsUnavailable(string field, string value)
    {
        var options = Configured();
        typeof(EmailOptions).GetProperty(field)!.SetValue(options, value);
        Assert.False(options.SmtpConfigured);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void InvalidPortsAreRejected(int port)
    {
        var options = Configured();
        options.Port = port;
        Assert.False(options.SmtpConfigured);
    }

    [Theory]
    [InlineData("https://interview.example.com", true)]
    [InlineData("http://localhost:5088", true)]
    [InlineData("http://127.0.0.1:5088/", true)]
    [InlineData("http://interview.example.com", false)]
    [InlineData("https://user:password@interview.example.com", false)]
    [InlineData("https://interview.example.com/another-app", false)]
    [InlineData("https://interview.example.com/?redirect=evil", false)]
    [InlineData("https://interview.example.com/#fragment", false)]
    [InlineData("https://interview.example.com\r\n", false)]
    [InlineData(" https://interview.example.com", false)]
    public void PublicUrlsRequireAnOriginAndHttpsOutsideLoopback(string url, bool valid)
        => Assert.Equal(valid, new EmailOptions { PublicBaseUrl = url }.HasValidPublicUrl);

    [Theory]
    [InlineData("victim@example.com\r\nBcc: injected@example.com", "Welcome")]
    [InlineData("victim@example.com,injected@example.com", "Welcome")]
    [InlineData("Recipient <victim@example.com>", "Welcome")]
    [InlineData("victim@example.com", "Welcome\r\nBcc: injected@example.com")]
    [InlineData("victim@example.com", "Welcome\u0000injected")]
    [InlineData("victim@example.com", "")]
    public async Task InvalidHeadersAreRejectedBeforeOpeningAnyConnection(string recipient, string subject)
    {
        var calls = 0;
        var sender = new SmtpAccountEmailSender(Options.Create(Configured()), NullLogger<SmtpAccountEmailSender>.Instance,
            () => { calls++; return new SmtpClient(); });
        var error = await Assert.ThrowsAsync<AccountEmailException>(() => sender.SendAsync(recipient, subject, "<p>Body</p>", "Body"));
        Assert.Equal("invalid_message", error.Code);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("StartTls", SecureSocketOptions.StartTls)]
    [InlineData("SslOnConnect", SecureSocketOptions.SslOnConnect)]
    public async Task DeliveryUsesRequiredTlsAndSnapshotSettingsAndBothBodyFormats(string security, SecureSocketOptions expected)
    {
        var options = Configured();
        options.Security = security;
        using var client = new RecordingClient();
        var sender = CreateSender(options, client);
        options.Enabled = false;
        options.Host = "mutated.example.com";
        options.Password = "mutated-password";
        options.FromAddress = "mutated@example.com";
        options.Security = "None";

        Assert.True(sender.IsConfigured);
        await sender.SendAsync("recipient@example.com", "Xác nhận email", "<p>Xin chào</p>", "Xin chào");

        Assert.Equal(new[] { "connect", "authenticate", "send", "disconnect" }, client.Calls);
        Assert.Equal("smtp.example.com", client.Host);
        Assert.Equal(expected, client.Security);
        Assert.Equal(15000, client.Timeout);
        Assert.Equal("test-only-password", client.Password);
        Assert.Null(client.ServerCertificateValidationCallback);
        Assert.True(client.Cancellable);
        Assert.Equal("sender@example.com", client.From);
        Assert.Equal("recipient@example.com", client.Recipient);
        Assert.Equal("Xác nhận email", client.Subject);
        Assert.Equal("<p>Xin chào</p>", client.Html);
        Assert.Equal("Xin chào", client.Text);
    }

    [Fact]
    public async Task UnconfiguredSenderDoesNotBecomeEnabledByMutatingOriginalOptions()
    {
        var options = Configured();
        options.Enabled = false;
        using var client = new RecordingClient();
        var sender = CreateSender(options, client);
        options.Enabled = true;
        var error = await Assert.ThrowsAsync<AccountEmailException>(() => sender.SendAsync("recipient@example.com", "Welcome", "<p>Body</p>", "Body"));
        Assert.Equal("not_configured", error.Code);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task ProviderDetailsAreAbsentFromLogsAndPublicErrors()
    {
        const string privateDetails = "test-only-password recipient@example.com private-token";
        using var client = new RecordingClient { ConnectFailure = new IOException(privateDetails) };
        var logger = new RecordingLogger();
        var sender = new SmtpAccountEmailSender(Options.Create(Configured()), logger, () => client);
        var error = await Assert.ThrowsAsync<AccountEmailException>(() => sender.SendAsync("recipient@example.com", "Welcome", "private-token", "private-token"));
        Assert.Equal("delivery_failed", error.Code);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(privateDetails, error.ToString());
        Assert.Equal(new[] { "Email delivery failed: delivery_failed" }, logger.Messages);
        Assert.All(logger.Exceptions, Assert.Null);
    }

    [Fact]
    public async Task AcceptedMessageStaysSuccessfulWhenQuitIsRejected()
    {
        using var client = new RecordingClient
        {
            DisconnectFailure = new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode,
                SmtpStatusCode.ServiceNotAvailable, "QUIT rejected after DATA accepted")
        };
        await CreateSender(Configured(), client).SendAsync("recipient@example.com", "Welcome", "<p>Body</p>", "Body");
        Assert.Equal(new[] { "connect", "authenticate", "send", "disconnect" }, client.Calls);
    }

    [Fact]
    public async Task StalledConnectionIsCancelledByTheFifteenSecondDeliveryDeadline()
    {
        using var client = new RecordingClient { StallConnect = true };
        var sender = CreateSender(Configured(), client);
        var error = await Assert.ThrowsAsync<AccountEmailException>(() =>
            sender.SendAsync("recipient@example.com", "Welcome", "<p>Body</p>", "Body")
                .WaitAsync(TimeSpan.FromSeconds(25)));
        Assert.Equal("timeout", error.Code);
        Assert.Equal(new[] { "connect" }, client.Calls);
    }

    [Fact]
    public async Task ServerWithoutStartTlsCannotReceiveCredentialsOrMessage()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var commands = new List<string>();
        var server = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 localhost test SMTP");
            while (await reader.ReadLineAsync(deadline.Token) is { } line)
            {
                commands.Add(line);
                if (line.StartsWith("EHLO ", StringComparison.OrdinalIgnoreCase))
                    await writer.WriteLineAsync("250-localhost\r\n250 AUTH PLAIN LOGIN");
                else await writer.WriteLineAsync("500 Unexpected command");
            }
        }, deadline.Token);
        var options = Configured();
        options.Host = "127.0.0.1";
        options.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var sender = new SmtpAccountEmailSender(Options.Create(options), NullLogger<SmtpAccountEmailSender>.Instance);
        var error = await Assert.ThrowsAsync<AccountEmailException>(() => sender.SendAsync("recipient@example.com", "Welcome", "private-token", "private-token", deadline.Token));
        await server.WaitAsync(deadline.Token);
        Assert.Equal("delivery_failed", error.Code);
        Assert.Contains(commands, command => command.StartsWith("EHLO ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(commands, command => command.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("MAIL", StringComparison.OrdinalIgnoreCase) || command.StartsWith("DATA", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UntrustedTlsCertificateIsRejected()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var server = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync(deadline.Token);
            using var tls = new SslStream(connection.GetStream());
            try { await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, deadline.Token); }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException) { }
        }, deadline.Token);
        var options = Configured();
        options.Host = "127.0.0.1";
        options.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        options.Security = "SslOnConnect";
        var sender = new SmtpAccountEmailSender(Options.Create(options), NullLogger<SmtpAccountEmailSender>.Instance);
        var error = await Assert.ThrowsAsync<AccountEmailException>(() => sender.SendAsync("recipient@example.com", "Welcome", "<p>Body</p>", "Body", deadline.Token));
        await server.WaitAsync(deadline.Token);
        Assert.Equal("delivery_failed", error.Code);
    }

    private static SmtpAccountEmailSender CreateSender(EmailOptions options, RecordingClient client)
        => new(Options.Create(options), NullLogger<SmtpAccountEmailSender>.Instance, () => client);

    private sealed class RecordingClient : SmtpClient
    {
        public List<string> Calls { get; } = [];
        public string? Host { get; private set; }
        public SecureSocketOptions Security { get; private set; }
        public string? Password { get; private set; }
        public bool Cancellable { get; private set; }
        public string? From { get; private set; }
        public string? Recipient { get; private set; }
        public string? Subject { get; private set; }
        public string? Html { get; private set; }
        public string? Text { get; private set; }
        public Exception? ConnectFailure { get; init; }
        public Exception? DisconnectFailure { get; init; }
        public bool StallConnect { get; init; }

        public override async Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken cancellationToken = default)
        {
            Calls.Add("connect"); Host = host; Security = options; Cancellable = cancellationToken.CanBeCanceled;
            if (ConnectFailure is not null) throw ConnectFailure;
            if (StallConnect) await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
        }

        public override Task AuthenticateAsync(Encoding encoding, ICredentials credentials, CancellationToken cancellationToken = default)
        {
            Calls.Add("authenticate");
            Password = credentials.GetCredential(new Uri("smtp://smtp.example.com"), "PLAIN")?.Password;
            return Task.CompletedTask;
        }

        public override Task<string> SendAsync(FormatOptions options, MimeMessage message, CancellationToken cancellationToken = default, ITransferProgress? progress = null)
        {
            Calls.Add("send");
            From = message.From.Mailboxes.Single().Address; Recipient = message.To.Mailboxes.Single().Address;
            Subject = message.Subject; Html = message.HtmlBody; Text = message.TextBody;
            return Task.FromResult("250 Message accepted");
        }

        public override Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
        {
            Calls.Add("disconnect");
            return DisconnectFailure is null ? Task.CompletedTask : Task.FromException(DisconnectFailure);
        }
    }

    private sealed class RecordingLogger : ILogger<SmtpAccountEmailSender>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception)); Exceptions.Add(exception);
        }
    }
}
