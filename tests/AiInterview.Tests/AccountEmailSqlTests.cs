using System.Collections.Concurrent;
using System.Text;
using AiInterview.Application.Interfaces;
using AiInterview.Infrastructure.Email;
using AiInterview.Infrastructure.Identity;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.Controllers;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AiInterview.Tests;

// Real SQL Server and Identity tokens/cookies; mail stays in memory and never leaves the tests.
public sealed class AccountEmailSqlTests
{
    [SqlFact]
    public async Task RegistrationPersistsPendingAccountAndRequiresValidConfirmationBeforeLogin()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        await using var app = Harness.Create(sql);
        using var request = app.Request();
        var input = Registration();
        input.FullName = "<img src=x onerror=alert(1)>";
        var result = Assert.IsType<RedirectToActionResult>(await request.Controller.Register(input));
        Assert.Equal("CheckEmail", result.ActionName);
        Assert.Equal(0, request.Context.Response.Headers.SetCookie.Count);
        var user = (await request.Users.FindByEmailAsync(input.Email))!;
        Assert.True(user.EmailConfirmationRequired);
        Assert.False(user.EmailConfirmed);
        Assert.Equal(1, await request.Db.StudentProfiles.CountAsync(x => x.UserId == user.Id));
        Assert.True((await request.SignIn.CheckPasswordSignInAsync(user, input.Password, true)).IsNotAllowed);
        var mail = Assert.Single(app.Mail.Messages);
        Assert.Equal(input.Email, mail.Recipient);
        Assert.DoesNotContain("<img", mail.Html);
        Assert.Contains("&lt;img", mail.Html);
        Assert.DoesNotContain(input.Password, mail.Text);
        var link = ConfirmationLink(mail);
        Assert.Equal("https://interview.example.test", link.GetLeftPart(UriPartial.Authority));
        Assert.DoesNotContain("untrusted-host", link.AbsoluteUri);
        var query = QueryHelpers.ParseQuery(link.Query);
        Assert.Equal(user.Id, query["userId"]);
        Assert.False(Result(await request.Controller.ConfirmEmail(user.Id, "invalid-code")).Succeeded);
        Assert.False(user.EmailConfirmed);
        Assert.True(Result(await request.Controller.ConfirmEmail(user.Id, query["code"])).Succeeded);
        Assert.True(user.EmailConfirmed);
        Assert.Equal(0, request.Context.Response.Headers.SetCookie.Count); // Clicking a link is not a login.
        Assert.Equal("no-referrer", request.Context.Response.Headers["Referrer-Policy"]);
        request.Controller.ModelState.Clear();
        var login = Assert.IsType<RedirectToActionResult>(await request.Controller.Login(new LoginModel { Email = input.Email, Password = input.Password }));
        Assert.Equal("Dashboard", login.ControllerName);
        Assert.True(request.Context.Response.Headers.SetCookie.Count > 0);
        Assert.Equal(2, app.Mail.Messages.Count);
        var notification = app.Mail.Messages.Last();
        Assert.Contains("12:30:00, 12/10/2026", notification.Text);
        Assert.DoesNotContain(input.Password, notification.Text);
        Assert.DoesNotContain("<img", notification.Html);
    }

    [SqlFact]
    public async Task FailedDeliveryKeepsPendingAccountAndResendWorksAfterCooldown()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        await using var app = Harness.Create(sql);
        using var request = app.Request();
        var input = Registration();
        app.Mail.Fail = true;
        Assert.IsType<RedirectToActionResult>(await request.Controller.Register(input));
        var user = (await request.Users.FindByEmailAsync(input.Email))!;
        Assert.False(user.EmailConfirmed);
        Assert.False(request.Controller.TempData["ConfirmationFailed"] is true); // Neutral completion avoids exposing account existence.
        Assert.Equal(0, request.Context.Response.Headers.SetCookie.Count);
        app.Mail.Fail = false;
        Assert.Equal(AccountMailResult.Throttled, await request.Email.SendConfirmationAsync(user));
        app.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(AccountMailResult.Sent, await request.Email.SendConfirmationAsync(user));
        Assert.Single(app.Mail.Messages);
        Assert.Equal(1, await request.Db.Users.CountAsync());
        Assert.Equal(1, await request.Db.StudentProfiles.CountAsync());
    }

    [SqlFact]
    public async Task UnconfiguredEmailStillAllowsRegistrationAndLoginWithoutClaimingEmailWasVerified()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        await using var app = Harness.Create(sql);
        app.Mail.IsConfigured = false;
        using var request = app.Request();
        var input = Registration();
        var result = Assert.IsType<RedirectToActionResult>(await request.Controller.Register(input));
        Assert.Equal("Login", result.ActionName);
        Assert.True(request.Controller.ModelState.IsValid);
        var user = Assert.Single(await request.Db.Users.ToListAsync());
        Assert.False(user.EmailConfirmationRequired);
        Assert.False(user.EmailConfirmed); // Never falsely mark an unverified address as confirmed.
        Assert.Equal(1, await request.Db.StudentProfiles.CountAsync(x => x.UserId == user.Id));
        Assert.True((await request.SignIn.CheckPasswordSignInAsync(user, input.Password, true)).Succeeded);
        Assert.Equal(0, request.Context.Response.Headers.SetCookie.Count); // Registration does not silently log in.
        Assert.Contains("chưa được xác minh", Assert.IsType<string>(request.Controller.TempData["Message"]));
        Assert.Empty(app.Mail.Messages);
    }

    [SqlFact]
    public async Task OldAccountsStillLoginWithoutFalselyMarkingThemConfirmedAndMailFailureDoesNotUndoLogin()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        await using var app = Harness.Create(sql);
        using var request = app.Request();
        var user = await request.CreateUser(confirmed: false, requiresConfirmation: false);
        app.Mail.Fail = true;
        Assert.IsType<RedirectToActionResult>(await request.Controller.Login(Login(user)));
        Assert.True(request.Context.Response.Headers.SetCookie.Count > 0);
        Assert.NotNull(request.Controller.TempData["Message"]);
        await request.Db.Entry(user).ReloadAsync();
        Assert.False(user.EmailConfirmed);
        Assert.False(user.EmailConfirmationRequired);
        Assert.Empty(app.Mail.Messages);
    }

    [SqlFact]
    public async Task WrongPasswordAndLockedAccountsNeverReceiveSuccessfulLoginMail()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        await using var app = Harness.Create(sql);
        using var request = app.Request();
        var user = await request.CreateUser(confirmed: true);
        var input = Login(user);
        input.Password = "Wrong-Test-Password!2";
        Assert.IsType<ViewResult>(await request.Controller.Login(input));
        request.Controller.ModelState.Clear();
        Assert.True((await request.Users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(10))).Succeeded);
        Assert.IsType<ViewResult>(await request.Controller.Login(Login(user)));
        Assert.Equal(0, request.Context.Response.Headers.SetCookie.Count);
        Assert.Empty(app.Mail.Messages);
    }

    [SqlFact]
    public async Task ExpiredAndOtherUsersTokensCannotConfirmAnAccount()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        await using var app = Harness.Create(sql, tokenLifetime: TimeSpan.FromSeconds(-1));
        using var request = app.Request();
        var user = await request.CreateUser();
        var other = await request.CreateUser();
        var token = await request.Users.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        Assert.False(Result(await request.Controller.ConfirmEmail(user.Id, code)).Succeeded);
        Assert.False(Result(await request.Controller.ConfirmEmail(other.Id, code)).Succeeded);
        Assert.False(Result(await request.Controller.ConfirmEmail(user.Id, new string('x', 4097))).Succeeded);
        Assert.False(Result(await request.Controller.ConfirmEmail(user.Id, "%%%" )).Succeeded);
        Assert.False(user.EmailConfirmed);
        Assert.False(other.EmailConfirmed);
        Assert.Equal(0, request.Context.Response.Headers.SetCookie.Count);
    }

    [SqlFact]
    public async Task ValidTokenIsBoundToTheIntendedAccount()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        await using var app = Harness.Create(sql);
        using var request = app.Request();
        var user = await request.CreateUser();
        var other = await request.CreateUser();
        var token = await request.Users.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        Assert.False(Result(await request.Controller.ConfirmEmail(other.Id, code)).Succeeded);
        Assert.True(Result(await request.Controller.ConfirmEmail(user.Id, code)).Succeeded);
        Assert.False(other.EmailConfirmed);
    }

    [SqlFact]
    public async Task ConcurrentResendsReserveOnlyOneEmailAcrossRequests()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        await using var app = Harness.Create(sql);
        using var first = app.Request();
        var user = await first.CreateUser();
        using var second = app.Request();
        var sameUser = (await second.Users.FindByIdAsync(user.Id))!;
        var results = await Task.WhenAll(first.Email.SendConfirmationAsync(user), second.Email.SendConfirmationAsync(sameUser));
        Assert.Single(results, x => x == AccountMailResult.Sent);
        Assert.Single(results, x => x == AccountMailResult.Throttled);
        Assert.Single(app.Mail.Messages);
    }

    [SqlFact]
    public async Task ResendDoesNotDiscloseUnknownOrConfirmedAccountsOrReflectPostedMessages()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        await using var app = Harness.Create(sql);
        using var request = app.Request();
        var user = await request.CreateUser(confirmed: true);
        await request.Controller.ResendConfirmation(new ConfirmationEmailModel { Email = user.Email! });
        var knownMessage = request.Controller.TempData["ConfirmationMessage"];
        await request.Controller.ResendConfirmation(new ConfirmationEmailModel { Email = "unknown@example.test" });
        Assert.Equal(knownMessage, request.Controller.TempData["ConfirmationMessage"]);
        var pending = await request.CreateUser();
        app.Mail.Fail = true;
        await request.Controller.ResendConfirmation(new ConfirmationEmailModel { Email = pending.Email! });
        Assert.Equal(knownMessage, request.Controller.TempData["ConfirmationMessage"]);
        Assert.False(request.Controller.TempData["ConfirmationFailed"] is true);
        Assert.Empty(app.Mail.Messages);
        request.Controller.ModelState.AddModelError("Email", "Invalid");
        var result = Assert.IsType<ViewResult>(await request.Controller.ResendConfirmation(new ConfirmationEmailModel { Message = "untrusted", DeliveryFailed = true }));
        var model = Assert.IsType<ConfirmationEmailModel>(result.Model);
        Assert.Null(model.Message);
        Assert.False(model.DeliveryFailed);
    }

    [SqlFact]
    public async Task SuccessfulLoginPreservesLocalReturnUrlAndSendsExactlyOneNotification()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        await using var app = Harness.Create(sql);
        using var request = app.Request();
        var user = await request.CreateUser(confirmed: true);
        var input = Login(user);
        input.ReturnUrl = "/Interviews/Create";
        Assert.Equal(input.ReturnUrl, Assert.IsType<LocalRedirectResult>(await request.Controller.Login(input)).Url);
        Assert.Single(app.Mail.Messages);
        Assert.True(request.Context.Response.Headers.SetCookie.Count > 0);
    }

    [SqlFact]
    public async Task RegistrationDoesNotRevealWhetherEmailAlreadyExists()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        await using var app = Harness.Create(sql);
        var input = Registration();
        using var first = app.Request();
        var created = Assert.IsType<RedirectToActionResult>(await first.Controller.Register(input));
        var expected = first.Controller.TempData["ConfirmationMessage"];
        using var second = app.Request();
        var duplicate = Assert.IsType<RedirectToActionResult>(await second.Controller.Register(input));
        Assert.Equal(created.ActionName, duplicate.ActionName);
        Assert.Equal(expected, second.Controller.TempData["ConfirmationMessage"]);
        Assert.Equal(1, await second.Db.Users.CountAsync());
        Assert.Single(app.Mail.Messages);
    }

    private static RegisterModel Registration() => new()
    {
        Email = $"registration-{Guid.NewGuid():N}@example.test", FullName = "Nguyễn Kiểm Thử",
        Password = SqlIntegrationTests.SqlDatabase.Password, ConfirmPassword = SqlIntegrationTests.SqlDatabase.Password
    };
    private static LoginModel Login(ApplicationUser user) => new() { Email = user.Email!, Password = SqlIntegrationTests.SqlDatabase.Password };
    private static ConfirmEmailResultModel Result(IActionResult result) => Assert.IsType<ConfirmEmailResultModel>(Assert.IsType<ViewResult>(result).Model);
    private static Uri ConfirmationLink(MailMessage mail) => new(mail.Text.Split('\n').Single(x => x.StartsWith("https://", StringComparison.Ordinal)));

    private sealed record MailMessage(string Recipient, string Subject, string Html, string Text);
    private sealed class MemoryMail : IAccountEmailSender
    {
        public bool IsConfigured { get; set; } = true;
        public bool Fail { get; set; }
        public ConcurrentQueue<MailMessage> Messages { get; } = new();
        public Task SendAsync(string recipient, string subject, string htmlBody, string textBody, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new AccountEmailException("test-delivery-failure");
            Messages.Enqueue(new(recipient, subject, htmlBody, textBody));
            return Task.CompletedTask;
        }
    }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 12, 5, 30, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan interval) => now += interval;
    }
    private sealed class Harness(ServiceProvider services, MemoryMail mail, TestClock clock) : IAsyncDisposable
    {
        public MemoryMail Mail => mail;
        public TestClock Clock => clock;
        public RequestScope Request() => new(services.CreateScope());
        public static Harness Create(SqlIntegrationTests.SqlDatabase sql, TimeSpan? tokenLifetime = null)
        {
            using var db = sql.Context();
            var connectionString = db.Database.GetConnectionString();
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddDataProtection().UseEphemeralDataProtectionProvider();
            collection.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
            collection.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = true;
            }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
            collection.AddScoped<IUserConfirmation<ApplicationUser>, AccountEmailConfirmation>();
            collection.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = tokenLifetime ?? TimeSpan.FromHours(24));
            collection.Configure<EmailOptions>(options => options.PublicBaseUrl = "https://interview.example.test");
            var mail = new MemoryMail();
            var clock = new TestClock();
            collection.AddSingleton<IAccountEmailSender>(mail);
            collection.AddSingleton<TimeProvider>(clock);
            collection.AddScoped<AccountEmailService>();
            return new(collection.BuildServiceProvider(), mail, clock);
        }
        public ValueTask DisposeAsync() => services.DisposeAsync();
    }
    private sealed class RequestScope : IDisposable
    {
        private readonly IServiceScope scope;
        public DefaultHttpContext Context { get; }
        public AccountController Controller { get; }
        public UserManager<ApplicationUser> Users { get; }
        public SignInManager<ApplicationUser> SignIn { get; }
        public AppDbContext Db { get; }
        public AccountEmailService Email { get; }
        public RequestScope(IServiceScope scope)
        {
            this.scope = scope;
            var services = scope.ServiceProvider;
            Context = new DefaultHttpContext { RequestServices = services };
            Context.Request.Host = new HostString("untrusted-host.example.test");
            services.GetRequiredService<IHttpContextAccessor>().HttpContext = Context;
            Users = services.GetRequiredService<UserManager<ApplicationUser>>();
            SignIn = services.GetRequiredService<SignInManager<ApplicationUser>>();
            SignIn.Context = Context;
            Db = services.GetRequiredService<AppDbContext>();
            Email = services.GetRequiredService<AccountEmailService>();
            var context = new ControllerContext
            {
                HttpContext = Context,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
                ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()
            };
            Controller = new(Users, SignIn, Db, Email)
            {
                ControllerContext = context, Url = new UrlHelper(context),
                TempData = new TempDataDictionary(Context, new MemoryTempData())
            };
        }
        public async Task<ApplicationUser> CreateUser(bool confirmed = false, bool requiresConfirmation = true)
        {
            var address = $"email-{Guid.NewGuid():N}@example.test";
            var user = new ApplicationUser { UserName = address, Email = address, FullName = "Kiểm thử", EmailConfirmed = confirmed, EmailConfirmationRequired = requiresConfirmation };
            var result = await Users.CreateAsync(user, SqlIntegrationTests.SqlDatabase.Password);
            Assert.True(result.Succeeded, string.Join(",", result.Errors.Select(x => x.Code)));
            return user;
        }
        public void Dispose() => scope.Dispose();
    }
    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
