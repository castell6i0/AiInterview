using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.Identity;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.Controllers;
using AiInterview.Web.Services;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AiInterview.Tests;

public sealed class AdminUserSqlIntegrationTests
{
    [SqlFact]
    public async Task CreateUsesIdentityPasswordsExplicitRolesAndStudentProfiles()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        foreach (var role in new[] { Roles.Student, Roles.Lecturer, Roles.Admin })
        {
            var model = NewUser(role);
            model.FullName = "  Nguyễn Văn Kiểm Thử  ";
            model.Email = $"  {role}@example.test  ";
            string id;
            await using (var scope = Scope(sql)) id = await scope.Admin.CreateUserAsync(model);
            await using var verify = Scope(sql);
            var saved = (await verify.Users.FindByIdAsync(id))!;
            Assert.Equal("Nguyễn Văn Kiểm Thử", saved.FullName);
            Assert.Equal($"{role}@example.test", saved.Email);
            Assert.Equal(saved.Email, saved.UserName);
            Assert.Equal(saved.Email!.ToUpperInvariant(), saved.NormalizedEmail);
            Assert.Equal(saved.NormalizedEmail, saved.NormalizedUserName);
            Assert.False(saved.EmailConfirmed);
            Assert.NotEqual(model.Password, saved.PasswordHash);
            Assert.True(await verify.Users.CheckPasswordAsync(saved, model.Password));
            Assert.Equal(role, Assert.Single(await verify.Users.GetRolesAsync(saved)));
            var profile = await verify.Db.StudentProfiles.SingleOrDefaultAsync(x => x.UserId == id);
            if (role == Roles.Student) Assert.Equal(saved.FullName, profile!.FullName);
            else Assert.Null(profile);
        }
    }

    [SqlFact]
    public async Task CreateRejectsWeakPasswordsDuplicatesAndUnknownRolesWithoutPartialAccounts()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var original = NewUser();
        await using (var scope = Scope(sql)) await scope.Admin.CreateUserAsync(original);
        var duplicate = NewUser();
        duplicate.Email = original.Email.ToUpperInvariant();
        await RejectCreate(sql, duplicate, "Email");
        var weak = NewUser();
        weak.Password = weak.ConfirmPassword = "alllowercasepassword";
        await RejectCreate(sql, weak, "Mật khẩu");
        var shortPassword = NewUser();
        shortPassword.Password = shortPassword.ConfirmPassword = "Aa1!short";
        await RejectCreate(sql, shortPassword, "Mật khẩu");
        var unknownRole = NewUser("SuperAdministrator");
        await RejectCreate(sql, unknownRole, "Vai trò");
        var invalidEmail = NewUser();
        invalidEmail.Email = "invalid-email";
        await RejectCreate(sql, invalidEmail, "Email");
        await using var verify = Scope(sql);
        Assert.Equal(1, await verify.Db.Users.CountAsync());
        Assert.Equal(1, await verify.Db.UserRoles.CountAsync());
        Assert.Equal(1, await verify.Db.StudentProfiles.CountAsync());
    }

    [SqlFact]
    public async Task EditSynchronizesIdentityAndStudentNameAndRollsBackDuplicateEmail()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var source = NewUser();
        var other = NewUser();
        string id;
        string securityStamp;
        await using (var scope = Scope(sql))
        {
            id = await scope.Admin.CreateUserAsync(source);
            await scope.Admin.CreateUserAsync(other);
            var original = (await scope.Users.FindByIdAsync(id))!;
            original.EmailConfirmed = true;
            Assert.True((await scope.Users.UpdateAsync(original)).Succeeded);
            securityStamp = original.SecurityStamp!;
        }
        await using (var scope = Scope(sql)) await scope.Admin.EditUserAsync(new EditUserVm
        {
            UserId = id, FullName = "  Họ Tên Mới  ", Email = "  Changed.Email@example.test  "
        });
        await using (var verify = Scope(sql))
        {
            var saved = (await verify.Users.FindByIdAsync(id))!;
            Assert.Equal("Họ Tên Mới", saved.FullName);
            Assert.Equal("Changed.Email@example.test", saved.Email);
            Assert.Equal(saved.Email, saved.UserName);
            Assert.Equal("CHANGED.EMAIL@EXAMPLE.TEST", saved.NormalizedEmail);
            Assert.Equal(saved.NormalizedEmail, saved.NormalizedUserName);
            Assert.False(saved.EmailConfirmed);
            Assert.NotEqual(securityStamp, saved.SecurityStamp);
            Assert.True(await verify.Users.CheckPasswordAsync(saved, source.Password));
            Assert.Equal(saved.FullName, (await verify.Db.StudentProfiles.SingleAsync(p => p.UserId == id)).FullName);
        }
        await using (var scope = Scope(sql))
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Admin.EditUserAsync(new EditUserVm
            {
                UserId = id, FullName = "Must not persist", Email = other.Email
            }));
        await using var fresh = Scope(sql);
        var unchanged = (await fresh.Users.FindByIdAsync(id))!;
        Assert.Equal("Họ Tên Mới", unchanged.FullName);
        Assert.Equal("Changed.Email@example.test", unchanged.Email);
        Assert.Equal("Họ Tên Mới", (await fresh.Db.StudentProfiles.SingleAsync(p => p.UserId == id)).FullName);
    }

    [SqlFact]
    public async Task DeleteUnusedAccountRemovesOnlyItsProfileAndIdentityRecords()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        string actor;
        string target;
        await using (var scope = Scope(sql))
        {
            actor = await scope.Admin.CreateUserAsync(NewUser(Roles.Admin));
            target = await scope.Admin.CreateUserAsync(NewUser());
            var user = (await scope.Users.FindByIdAsync(target))!;
            Assert.True((await scope.Users.AddClaimAsync(user, new System.Security.Claims.Claim("test", "metadata"))).Succeeded);
        }
        await using (var scope = Scope(sql))
        {
            Assert.True((await scope.Admin.DeletionAsync(target, actor))!.CanDelete);
            await scope.Admin.DeleteUserAsync(target, actor);
        }
        await using var verify = Scope(sql);
        Assert.Null(await verify.Users.FindByIdAsync(target));
        Assert.NotNull(await verify.Users.FindByIdAsync(actor));
        Assert.False(await verify.Db.StudentProfiles.AnyAsync(p => p.UserId == target));
        Assert.False(await verify.Db.UserRoles.AnyAsync(r => r.UserId == target));
        Assert.False(await verify.Db.UserClaims.AnyAsync(c => c.UserId == target));
        Assert.Equal(63, await verify.Db.Questions.CountAsync());
    }

    [SqlFact]
    public async Task DeleteRefusesAccountsWithCvJdSessionAiOrClassHistory()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        string actor;
        await using (var scope = Scope(sql)) actor = await scope.Admin.CreateUserAsync(NewUser(Roles.Admin));
        foreach (var history in new[] { "cv", "jd", "session", "ai", "class", "membership" })
        {
            string target;
            await using (var scope = Scope(sql))
            {
                target = await scope.Admin.CreateUserAsync(NewUser());
                switch (history)
                {
                    case "cv": scope.Db.CvProfiles.Add(new CvProfile { StudentId = target, FileName = "cv.pdf" }); break;
                    case "jd": scope.Db.JobDescriptions.Add(new JobDescription { StudentId = target, Title = "Saved JD" }); break;
                    case "session":
                        scope.Db.Sessions.Add(new InterviewSession
                        {
                            StudentId = target, PositionId = await scope.Db.Positions.Select(p => p.Id).FirstAsync(),
                            RubricVersionId = await scope.Db.RubricVersions.Where(r => r.IsActive).Select(r => r.Id).SingleAsync()
                        });
                        break;
                    case "ai": scope.Db.AiRequestLogs.Add(new AiRequestLog { UserId = target, Operation = "cv-analysis", Status = "Success" }); break;
                    case "class": scope.Db.Classes.Add(new Classroom { LecturerId = target, Name = "History", JoinCode = Guid.NewGuid().ToString("N")[..20] }); break;
                    case "membership":
                        scope.Db.Classes.Add(new Classroom { LecturerId = actor, Name = "Class membership", JoinCode = Guid.NewGuid().ToString("N")[..20],
                            Members = [new ClassMember { StudentId = target }] });
                        break;
                }
                await scope.Db.SaveChangesAsync();
            }
            await using (var scope = Scope(sql))
            {
                var deletion = (await scope.Admin.DeletionAsync(target, actor))!;
                Assert.False(deletion.CanDelete);
                Assert.Contains("lịch sử", deletion.RefusalReason);
                await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Admin.DeleteUserAsync(target, actor));
            }
            await using var verify = Scope(sql);
            Assert.NotNull(await verify.Users.FindByIdAsync(target));
            Assert.True(await verify.Db.StudentProfiles.AnyAsync(p => p.UserId == target));
        }
    }

    [SqlFact]
    public async Task SelfDeletionAndRemovalOfTheLastActiveAdministratorAreBlocked()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        string active;
        string lockedBackup;
        await using (var scope = Scope(sql))
        {
            active = await scope.Admin.CreateUserAsync(NewUser(Roles.Admin));
            lockedBackup = await scope.Admin.CreateUserAsync(NewUser(Roles.Admin));
            await scope.Admin.UpdateUserAsync(new UserAccessVm { UserId = lockedBackup, Role = Roles.Admin, Locked = true }, active);
        }
        await using (var scope = Scope(sql))
        {
            Assert.False((await scope.Admin.DeletionAsync(active, active))!.CanDelete);
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Admin.DeleteUserAsync(active, active));
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Admin.UpdateUserAsync(
                new UserAccessVm { UserId = active, Role = Roles.Student }, active));
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Admin.UpdateUserAsync(
                new UserAccessVm { UserId = active, Role = Roles.Admin, Locked = true }, active));
        }
        await using (var scope = Scope(sql))
        {
            // The service safeguard is checked independently of the controller's authorization.
            var deletion = (await scope.Admin.DeletionAsync(active, lockedBackup))!;
            Assert.False(deletion.CanDelete);
            Assert.Contains("ít nhất một", deletion.RefusalReason);
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Admin.DeleteUserAsync(active, lockedBackup));
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Admin.UpdateUserAsync(
                new UserAccessVm { UserId = active, Role = Roles.Student }, lockedBackup));
        }
        await using var verify = Scope(sql);
        var lastAdmin = (await verify.Users.FindByIdAsync(active))!;
        Assert.True(await verify.Users.IsInRoleAsync(lastAdmin, Roles.Admin));
        Assert.False(await verify.Users.IsLockedOutAsync(lastAdmin));
        Assert.Equal(2, await verify.Db.Users.CountAsync());
    }

    [Fact]
    public void AllAdminUserMutationsRequireAdminAuthorizationAndAntiforgery()
    {
        var controller = typeof(AdminController);
        var authorization = Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(Roles.Admin, authorization.Roles);
        foreach (var name in new[] { nameof(AdminController.CreateUser), nameof(AdminController.EditUser), nameof(AdminController.DeleteUserConfirmed), nameof(AdminController.UserAccess) })
        {
            var post = Assert.Single(controller.GetMethods(), m => m.Name == name && m.IsDefined(typeof(HttpPostAttribute), true));
            Assert.True(post.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true));
        }
    }

    private static CreateUserVm NewUser(string role = Roles.Student) => new()
    {
        FullName = "Người dùng kiểm thử", Email = $"admin-test-{Guid.NewGuid():N}@example.test",
        Password = SqlIntegrationTests.SqlDatabase.Password, ConfirmPassword = SqlIntegrationTests.SqlDatabase.Password, Role = role
    };

    private static async Task RejectCreate(SqlIntegrationTests.SqlDatabase sql, CreateUserVm model, string expected)
    {
        await using var scope = Scope(sql);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Admin.CreateUserAsync(model));
        Assert.Contains(expected, error.Message);
    }

    private static AdminScope Scope(SqlIntegrationTests.SqlDatabase sql) => new(sql.Services.CreateAsyncScope());

    private sealed class AdminScope : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        public AppDbContext Db { get; }
        public UserManager<ApplicationUser> Users { get; }
        public AdminService Admin { get; }
        public AdminScope(AsyncServiceScope scope)
        {
            this.scope = scope;
            Db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Admin = new AdminService(Db, Users);
        }
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
