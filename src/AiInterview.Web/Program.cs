using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using AiInterview.Application.Interfaces;
using AiInterview.Application.Services;
using AiInterview.Infrastructure.AI;
using AiInterview.Infrastructure.Documents;
using AiInterview.Infrastructure.Email;
using AiInterview.Infrastructure.Identity;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
if (builder.Environment.IsDevelopment()) builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Configuration.AddEnvironmentVariables();
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Thiếu connection string DefaultConnection.")));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedAccount = true;
    options.Password.RequiredLength = 10;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    options.Lockout.MaxFailedAccessAttempts = 5;
}).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.AddScoped<IUserConfirmation<ApplicationUser>, AccountEmailConfirmation>();
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(24));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAccountEmailSender, SmtpAccountEmailSender>();
builder.Services.AddScoped<AccountEmailService>();
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login"; options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "AiInterview.Auth"; options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(2); options.SlidingExpiration = true;
});
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add<AjaxRedirectFilter>();
});
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
builder.Services.AddOptions<GeminiOptions>().Bind(builder.Configuration.GetSection("Gemini"))
    .Validate(x => x.MaxRequestsPerMinute is >= 1 and <= 1000 && x.MaxRetries is >= 0 and <= 3 && x.TimeoutSeconds is >= 10 and <= 300,
        "Gemini: kiểm tra MaxRequestsPerMinute (1..1000), MaxRetries (0..3), TimeoutSeconds (10..300).").ValidateOnStart();
builder.Services.AddOptions<OpenAiOptions>().Bind(builder.Configuration.GetSection("OpenAI"))
    .Validate(x => x.MaxRequestsPerMinute is >= 1 and <= 1000 && x.MaxRetries is >= 0 and <= 3 && x.TimeoutSeconds is >= 10 and <= 300,
        "OpenAI: kiểm tra MaxRequestsPerMinute (1..1000), MaxRetries (0..3), TimeoutSeconds (10..300).").ValidateOnStart();
builder.Services.Configure<AiProviderOptions>(builder.Configuration.GetSection("AI"));
var selectedAiProvider = builder.Configuration["AI:Provider"] ?? "Gemini";
if (selectedAiProvider is not ("Gemini" or "OpenAI")) throw new InvalidOperationException("AI:Provider chỉ nhận Gemini hoặc OpenAI.");
builder.Services.AddSingleton<AiRequestLimiter>();
builder.Services.AddHttpClient(nameof(GeminiAiService), client => client.Timeout = Timeout.InfiniteTimeSpan)
    .RedactLoggedHeaders(["x-goog-api-key"]);
builder.Services.AddHttpClient(nameof(OpenAiService), client => client.Timeout = Timeout.InfiniteTimeSpan)
    .RedactLoggedHeaders(["Authorization"]);
builder.Services.AddHttpClient("AiConnectionCheck", client => client.Timeout = TimeSpan.FromSeconds(45))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
    .RedactLoggedHeaders(["Authorization", "x-goog-api-key"]);
builder.Services.AddScoped<UserAiSettingsService>();
builder.Services.AddScoped<CurrentUserAiConfiguration>();
builder.Services.AddScoped(services => new AiConnectionCheckService(
    services.GetRequiredService<IHttpClientFactory>(), services.GetRequiredService<CurrentUserAiConfiguration>(),
    services.GetRequiredService<DbContextOptions<AppDbContext>>(), services.GetRequiredService<AiRequestLimiter>()));
builder.Services.AddScoped<IAiService>(services => services.GetRequiredService<CurrentUserAiConfiguration>().CreateService(
    services.GetRequiredService<IHttpClientFactory>(), services.GetRequiredService<DbContextOptions<AppDbContext>>(),
    services.GetRequiredService<AiRequestLimiter>()));
builder.Services.AddScoped<ICvTextExtractor, CvTextExtractor>();
builder.Services.AddScoped<IDeliveryAnalyticsService, DeliveryAnalyticsService>();
builder.Services.AddScoped<InterviewService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<LecturerService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<ClassroomService>();
builder.Services.AddRequestProtection(builder.Configuration);
builder.Services.AddOptions<RequestBodyLimitsOptions>().Bind(builder.Configuration.GetSection("RequestBodyLimits"))
    .Validate(RequestBodyLimitsOptions.IsValid, "RequestBodyLimits không hợp lệ.").ValidateOnStart();
builder.Services.AddOptions<StorageLimitsOptions>().Bind(builder.Configuration.GetSection("StorageLimits")).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<UserStorageLimiter>();
builder.Services.AddOptions<AiQuotaOptions>().Bind(builder.Configuration.GetSection("AiQuota")).ValidateDataAnnotations().ValidateOnStart();
// EF exceptions can contain SQL/schema even when sensitive data logging is disabled.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Update", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Query", LogLevel.None);
if (!builder.Environment.IsDevelopment() && (builder.Configuration.GetValue<bool>("Database:AutoMigrate") || builder.Configuration.GetValue<bool>("Seed:Enabled")))
    throw new InvalidOperationException("Production không cho bật Database:AutoMigrate hoặc Seed:Enabled. Hãy áp dụng migration riêng.");
var app = builder.Build();
var culture = CultureInfo.GetCultureInfo("vi-VN");
app.UseRequestLocalization(new RequestLocalizationOptions { DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(culture), SupportedCultures = [culture], SupportedUICultures = [culture] });
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Home/Error"); app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Permissions-Policy"] = "microphone=(self), camera=()";
    try { await next(); }
    catch (KeyNotFoundException) when (!context.Response.HasStarted) { context.Response.StatusCode = 404; await context.Response.WriteAsync("Không tìm thấy nội dung hoặc bạn không có quyền truy cập."); }
});
app.UseStaticFiles(); app.UseRouting();
app.UseMiddleware<SafeErrorMiddleware>();
app.UseAuthentication(); app.UseRateLimiter();
app.UseMiddleware<RequestBodyLimitMiddleware>();
app.Use(async (context, next) =>
{
    await context.RequestServices.GetRequiredService<CurrentUserAiConfiguration>()
        .ResolveAsync(context.User.FindFirstValue(ClaimTypes.NameIdentifier), context.RequestAborted);
    await next();
});
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
if (args.Contains("--migrate") || app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await new LocalDbStartup(new LocalDbStartupRuntime(), app.Services.GetRequiredService<ILogger<LocalDbStartup>>())
        .EnsureReadyAsync(app.Configuration.GetConnectionString("DefaultConnection")!, app.Environment.IsDevelopment(), app.Lifetime.ApplicationStopping);
    await using (var scope = app.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await DatabaseSeeder.SeedAsync(app.Services, app.Configuration, app.Environment.IsDevelopment());
    if (args.Contains("--migrate")) return;
}
app.Run();
public partial class Program { }
