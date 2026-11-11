using System.Data.Common;
using System.Security.Claims;
using AiInterview.Application.DTOs;
using AiInterview.Application.Interfaces;
using AiInterview.Application.Services;
using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.AI;
using AiInterview.Infrastructure.Identity;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.Controllers;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiInterview.Tests;

public sealed class DatabaseSecurityTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const string PreviousMigration = "20261013105437_AddPersonalAiSettings";

    [Fact]
    public void AllLargeTextColumnsHaveDatabaseLengthConstraints()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=MetadataOnly;Integrated Security=true").Options);
        var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var entity in model.GetEntityTypes().Where(x => x.ClrType.Namespace == typeof(InterviewSession).Namespace))
        foreach (var property in entity.GetProperties().Where(x => x.ClrType == typeof(string) && x.GetMaxLength() is null))
        {
            // Ownership columns are limited by their relationship to Identity's nvarchar(450) key.
            if (property.IsForeignKey() || property.IsPrimaryKey()) continue;
            var limit = Assert.Single(DatabaseTextLimits.All, x => x.Entity == entity.ClrType && x.Column == property.Name);
            Assert.Contains(entity.GetCheckConstraints(), c => c.Name == limit.Constraint && c.Sql.Contains("DATALENGTH"));
        }
    }

    [SqlFact]
    public async Task MigrationRetainsLegacyOversizedRowsAndRejectsNewOversizedWrites()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var user = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var legacy = new CvProfile { StudentId = user.Id, FileName = "legacy.pdf", ExtractedText = new string('x', 50_001) };
        db.CvProfiles.Add(legacy);
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(50_001, (await db.CvProfiles.SingleAsync()).ExtractedText.Length);
        db.CvProfiles.Add(new CvProfile { StudentId = user.Id, FileName = "new.pdf", ExtractedText = new string('x', 50_001) });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        // Trailing spaces must count; LEN alone would miss this violation.
        db.JobDescriptions.Add(new JobDescription { StudentId = user.Id, Title = "JD", Content = new string('x', 30_000) + " " });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.CvProfiles.Add(new CvProfile { StudentId = user.Id, FileName = "valid.pdf", ExtractedText = new string('x', 50_000) });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.CvProfiles.CountAsync());
        // Rollback remains non-destructive to CV/JD and re-upgrade is supported.
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await db.Database.MigrateAsync();
        Assert.Equal(2, await db.CvProfiles.CountAsync());
    }

    [SqlFact]
    public async Task ReportDeniesBeforeLoadingTurnsAndAllowsOnlyOwnerOrOwnClassLecturer()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        var outsider = await sql.CreateStudentAsync();
        var lecturer = await sql.CreateStudentAsync();
        await using var setup = sql.Context();
        var id = await Interviews(setup).CreateAsync(owner.Id, await Input(setup), Ct);
        var classroom = new Classroom { LecturerId = lecturer.Id, Name = "Security test", JoinCode = Guid.NewGuid().ToString("N")[..16] };
        classroom.Members.Add(new ClassMember { StudentId = owner.Id });
        setup.Classes.Add(classroom);
        await setup.SaveChangesAsync();
        var recorder = new QueryRecorder();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(setup.Database.GetConnectionString()).AddInterceptors(recorder).Options);
        var denied = Controller(db, outsider.Id, Roles.Student);
        Assert.IsType<NotFoundResult>(await denied.Report(id, Ct));
        var statement = Assert.Single(recorder.Queries);
        Assert.DoesNotContain("CvSnapshot", statement);
        Assert.DoesNotContain("Turns", statement);
        Assert.IsType<NotFoundResult>(await Controller(db, outsider.Id, Roles.Lecturer).Report(id, Ct));
        Assert.IsType<ViewResult>(await Controller(db, lecturer.Id, Roles.Lecturer).Report(id, Ct));
        Assert.IsType<RedirectToActionResult>(await Controller(db, owner.Id, Roles.Student).Report(id, Ct));
        setup.ClassMembers.Remove(await setup.ClassMembers.SingleAsync());
        await setup.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await Controller(db, lecturer.Id, Roles.Lecturer).Report(id, Ct));
    }

    [SqlFact]
    public async Task ConcurrentCvAndJdCreationCannotExceedStorageLimits()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var owner = await sql.CreateStudentAsync();
        var other = await sql.CreateStudentAsync();
        var gate = new UserStorageLimiter();
        var limits = Options.Create(new StorageLimitsOptions { MaxCvsPerUser = 1, MaxJdsPerUser = 1 });
        async Task<bool> Add(string userId, bool cv)
        {
            await using var db = sql.Context();
            await using var scope = sql.Services.CreateAsyncScope();
            var service = new ProfileService(db, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), new TextExtractor(), new UnusedAi(), limits, gate);
            try
            {
                if (cv)
                {
                    using var stream = new MemoryStream(new byte[] { 1 });
                    await service.UploadAsync(userId, new FormFile(stream, 0, 1, "cvFile", "test.pdf"), Ct);
                }
                else await service.CreateJdAsync(userId, new CreateJdModel { Title = "Test JD", Content = new string('x', 30) }, Ct);
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or CvExtractionException) { return false; }
        }
        Assert.Single(await Task.WhenAll(Add(owner.Id, true), Add(owner.Id, true)), x => x);
        Assert.Single(await Task.WhenAll(Add(owner.Id, false), Add(owner.Id, false)), x => x);
        Assert.True(await Add(other.Id, true));
        Assert.True(await Add(other.Id, false));
        await using var verify = sql.Context();
        Assert.Equal(1, await verify.CvProfiles.CountAsync(x => x.StudentId == owner.Id));
        Assert.Equal(1, await verify.JobDescriptions.CountAsync(x => x.StudentId == owner.Id));
    }

    [SqlFact]
    public async Task ConcurrentSessionCreationHonorsActiveAndDailyLimits()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var user = await sql.CreateStudentAsync();
        var limits = Options.Create(new StorageLimitsOptions { MaxActiveSessionsPerUser = 1, MaxSessionsPerDay = 2 });
        var gate = new UserStorageLimiter();
        async Task<Guid?> Create()
        {
            await using var db = sql.Context();
            try { return await Interviews(db, limits, gate).CreateAsync(user.Id, await Input(db), Ct); }
            catch (InvalidOperationException) { return null; }
        }
        var attempts = await Task.WhenAll(Create(), Create());
        var first = Assert.Single(attempts, x => x.HasValue)!.Value;
        await using var db = sql.Context();
        await Interviews(db, limits, gate).EndAsync(user.Id, first, Ct);
        var second = await Create();
        Assert.NotNull(second);
        await Interviews(db, limits, gate).EndAsync(user.Id, second!.Value, Ct);
        Assert.Null(await Create());
        Assert.Equal(2, await db.Sessions.CountAsync());
    }

    private static InterviewService Interviews(AppDbContext db, IOptions<StorageLimitsOptions>? limits = null, UserStorageLimiter? gate = null) =>
        new(db, new UnusedAi(), new DeliveryAnalyticsService(), Options.Create(new GeminiOptions()), limits, gate);

    private static async Task<CreateInterviewModel> Input(AppDbContext db) => new()
    { PositionId = await db.Positions.Select(x => x.Id).FirstAsync(), QuestionCount = 5, ConsentToAi = true, ConsentedProvider = "Gemini" };

    private static InterviewsController Controller(AppDbContext db, string user, string role) => new(Interviews(db), db, new UnusedAi())
    {
        ControllerContext = new ControllerContext
        { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Role, role) }, "test")) } }
    };

    private sealed class QueryRecorder : DbCommandInterceptor
    {
        public List<string> Queries { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Queries.Add(command.CommandText); return ValueTask.FromResult(result); }
    }

    private sealed class TextExtractor : ICvTextExtractor
    { public Task<string> ExtractAsync(Stream stream, string fileName, CancellationToken cancellationToken = default) => Task.FromResult("Synthetic CV text for storage limit tests only."); }

    private sealed class UnusedAi : IAiService
    {
        public string Provider => "Gemini";
        public string Model => "test-only";
        public bool IsConfigured => true;
        public Task<T> GenerateAsync<T>(AiRequest request, Func<T, string?> validate, Func<string, Task>? onText = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("No provider call is expected in database security tests.");
    }
}
