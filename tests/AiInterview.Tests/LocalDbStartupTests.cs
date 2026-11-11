using AiInterview.Web.Services;
using AiInterview.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiInterview.Tests;

public sealed class LocalDbStartupTests
{
    private const string Connection = @"Server=(localdb)\MSSQLLocalDB;Database=AiInterview;Integrated Security=true;Encrypt=true;TrustServerCertificate=true";

    [Theory]
    [InlineData(false, true, Connection)]
    [InlineData(true, false, Connection)]
    [InlineData(true, true, "Server=sql.example.test;Database=AiInterview;Integrated Security=true")]
    [InlineData(true, true, @"Server=(localdb)\MSSQLLocalDB;User ID=test;Password=synthetic")]
    [InlineData(true, true, @"Server=(localdb)\.\shared;Integrated Security=true")]
    [InlineData(true, true, @"Server=(localdb)\MSSQLLocalDB & exit;Integrated Security=true")]
    public async Task DoesNotManageProductionRemoteSharedOrUntrustedConfigurations(bool development, bool windows, string connection)
    {
        var runtime = new FakeRuntime { IsWindows = windows };
        await Create(runtime).EnsureReadyAsync(connection, development);
        Assert.Empty(runtime.Commands);
        Assert.Empty(runtime.Probes);
    }

    [Fact]
    public async Task ExistingInstanceIsStartedWithoutCreateDeleteOrChangingConfiguredDatabase()
    {
        var runtime = new FakeRuntime();
        await Create(runtime).EnsureReadyAsync(Connection, true);
        Assert.Equal(new[] { "info", "start MSSQLLocalDB" }, runtime.Commands);
        var probe = new SqlConnectionStringBuilder(Assert.Single(runtime.Probes));
        Assert.Equal("master", probe.InitialCatalog);
        Assert.Equal(@"(localdb)\MSSQLLocalDB", probe.DataSource);
        Assert.True(probe.IntegratedSecurity);
        Assert.False(probe.Pooling);
        Assert.Equal("AiInterview", new SqlConnectionStringBuilder(Connection).InitialCatalog);
    }

    [Fact]
    public async Task MissingNamedInstanceIsCreatedAndWaitsUntilReadyWithoutAttachingUserDatabase()
    {
        var runtime = new FakeRuntime { Instances = "OtherInstance\r\n" };
        runtime.Ready.Enqueue(false);
        runtime.Ready.Enqueue(false);
        runtime.Ready.Enqueue(true);
        await Create(runtime).EnsureReadyAsync(Connection + @";AttachDBFilename=C:\private\AiInterview.mdf", true);
        Assert.Equal(new[] { "info", "create MSSQLLocalDB", "start MSSQLLocalDB" }, runtime.Commands);
        Assert.Equal(2, runtime.Delays);
        Assert.All(runtime.Probes, probe => Assert.Empty(new SqlConnectionStringBuilder(probe).AttachDBFilename));
    }

    [Fact]
    public async Task ConcurrentCreateFailureCanRecoverWhenOtherProcessAlreadyStartedInstance()
    {
        var runtime = new FakeRuntime { Instances = "", CreateExitCode = 1, StartExitCode = 1 };
        await Create(runtime).EnsureReadyAsync(Connection, true);
        Assert.Single(runtime.Probes);
        Assert.Equal(new[] { "info", "create MSSQLLocalDB", "start MSSQLLocalDB" }, runtime.Commands);
    }

    [Fact]
    public async Task ExhaustedReadinessFailsClearlyAndNeverDeletesOrReplacesDatabase()
    {
        var runtime = new FakeRuntime { AlwaysUnready = true };
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(runtime).EnsureReadyAsync(Connection, true));
        Assert.Contains("SQL LocalDB 'MSSQLLocalDB'", exception.Message);
        Assert.Contains("Không xóa instance", exception.Message);
        Assert.Equal(12, runtime.Probes.Count);
        Assert.Equal(11, runtime.Delays);
        Assert.Equal(new[] { "info", "start MSSQLLocalDB" }, runtime.Commands);
    }

    [Fact]
    public async Task MissingUtilityHasInstallationGuidanceInsteadOfRawStartupException()
    {
        var runtime = new FakeRuntime { MissingUtility = true };
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(runtime).EnsureReadyAsync(Connection, true));
        Assert.Contains("Visual Studio Installer", exception.Message);
        Assert.Contains("SQL Server Express LocalDB", exception.Message);
    }

    [Fact]
    public async Task CallerCancellationIsPreserved()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(new FakeRuntime()).EnsureReadyAsync(Connection, true, cancellation.Token));
    }

    [SqlFact]
    public async Task RealRuntimeCreatesColdTestInstanceQueriesMasterAndAppliesMigrations()
    {
        var runtime = new LocalDbStartupRuntime();
        var instance = "AiInterview_StartupTest_" + Guid.NewGuid().ToString("N");
        var before = await runtime.RunAsync(["info"], default);
        Assert.Equal(0, before.ExitCode);
        Assert.DoesNotContain(instance, before.Output, StringComparison.OrdinalIgnoreCase);
        // Separate LocalDB instances can share the default data-file directory. Both names must be unique.
        var configured = $@"Server=(localdb)\{instance};Database={instance};Integrated Security=true;Encrypt=true;TrustServerCertificate=true;Pooling=false";
        var ready = false;
        try
        {
            await new LocalDbStartup(runtime, NullLogger<LocalDbStartup>.Instance).EnsureReadyAsync(configured, true);
            ready = true;
            var probe = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master", ConnectTimeout = 5 };
            Assert.True(await runtime.CanConnectAsync(probe.ConnectionString, default));
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(configured).Options);
            await db.Database.MigrateAsync();
            Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            // Drop only this test's unique database while SQL is running, so its files are removed too.
            // Deleting a LocalDB instance alone leaves user database files behind.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                if (ready)
                {
                    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(configured).Options);
                    await db.Database.EnsureDeletedAsync(cleanup.Token);
                }
            }
            finally
            {
                // Cleanup is limited to the random instance created by this test; never a user's instance.
                await runtime.RunAsync(["stop", instance], cleanup.Token);
                await runtime.RunAsync(["delete", instance], cleanup.Token);
            }
        }
    }

    private static LocalDbStartup Create(FakeRuntime runtime) => new(runtime, NullLogger<LocalDbStartup>.Instance);

    private sealed class FakeRuntime : ILocalDbStartupRuntime
    {
        public bool IsWindows { get; init; } = true;
        public string Instances { get; init; } = "mssqllocaldb\r\nAiInterviewDev\r\n";
        public bool AlwaysUnready { get; init; }
        public bool MissingUtility { get; init; }
        public int CreateExitCode { get; init; }
        public int StartExitCode { get; init; }
        public List<string> Commands { get; } = [];
        public List<string> Probes { get; } = [];
        public Queue<bool> Ready { get; } = [];
        public int Delays { get; private set; }

        public Task<LocalDbCommandResult> RunAsync(string[] arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MissingUtility) throw new System.ComponentModel.Win32Exception();
            Commands.Add(string.Join(" ", arguments));
            return Task.FromResult(new LocalDbCommandResult(arguments[0] switch
            { "create" => CreateExitCode, "start" => StartExitCode, _ => 0 }, Instances));
        }

        public Task<bool> CanConnectAsync(string connectionString, CancellationToken cancellationToken)
        {
            Probes.Add(connectionString);
            return Task.FromResult(!AlwaysUnready && (Ready.Count == 0 || Ready.Dequeue()));
        }

        public Task DelayAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays++;
            return Task.CompletedTask;
        }
    }
}
