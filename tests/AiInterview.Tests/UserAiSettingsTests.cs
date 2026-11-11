using System.Text.Json;
using AiInterview.Infrastructure.AI;
using AiInterview.Infrastructure.Identity;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiInterview.Tests;

public sealed class UserAiSettingsTests
{
    private const string GeminiKey = "synthetic-gemini-key-never-valid";
    private const string OpenAiKey = "synthetic-openai-key-never-valid";

    [Theory]
    [InlineData(" key-with-valid_printable.characters ", "key-with-valid_printable.characters")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void KeysAreTrimmedWithoutEchoingOrChangingContent(string? input, string expected) =>
        Assert.Equal(expected, UserAiSettingsService.ValidateApiKey(input));

    [Theory]
    [InlineData("key embedded space")]
    [InlineData("key\nline")]
    [InlineData("key\tvalue")]
    [InlineData("key\0value")]
    [InlineData("key-tiếng-việt")]
    [InlineData("key\u007fvalue")]
    public void UnsafeKeysProduceOnlySafeValidationErrors(string input)
    {
        var error = Assert.Throws<UserAiSettingsValidationException>(() => UserAiSettingsService.ValidateApiKey(input));
        Assert.Equal("ApiKey", error.Field);
        Assert.DoesNotContain(input, error.Message);
    }

    [Fact]
    public void KeyLengthIsBounded()
    {
        Assert.Equal(2048, UserAiSettingsService.ValidateApiKey(new string('k', 2048)).Length);
        Assert.Throws<UserAiSettingsValidationException>(() => UserAiSettingsService.ValidateApiKey(new string('k', 2049)));
    }

    [Theory]
    [InlineData("https://other.test/model")]
    [InlineData("models/gemini-flash")]
    [InlineData("../model")]
    [InlineData("model?key=value")]
    [InlineData("model\nheader")]
    [InlineData("")]
    public void ModelsCannotChangeEndpointOrInjectHeaders(string input) =>
        Assert.Throws<UserAiSettingsValidationException>(() => UserAiSettingsService.ValidateModel(input));

    [Fact]
    public void ValidModelIdentifiersMatchProviderRules()
    {
        Assert.Equal("gpt-4.1-mini", UserAiSettingsService.ValidateModel(" gpt-4.1-mini "));
        Assert.Equal("gemini-3.1-flash-lite", UserAiSettingsService.ValidateModel("gemini-3.1-flash-lite"));
        Assert.Equal(100, UserAiSettingsService.ValidateModel(new string('a', 100)).Length);
        Assert.Throws<UserAiSettingsValidationException>(() => UserAiSettingsService.ValidateModel(new string('a', 101)));
        Assert.Throws<UserAiSettingsValidationException>(() => UserAiSettingsService.ValidateProvider("custom"));
    }

    [Fact]
    public void SettingsHaveCascadeUserOwnershipAndOptimisticConcurrency()
    {
        using var db = ModelContext();
        var entity = db.Model.FindEntityType(typeof(UserAiSettings))!;
        Assert.Equal("UserId", Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        var owner = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(ApplicationUser), owner.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, owner.DeleteBehavior);
        Assert.True(entity.FindProperty(nameof(UserAiSettings.RowVersion))!.IsConcurrencyToken);
    }

    [Fact]
    public async Task ServerOptionsAreCopiedOnceAndProbeUsesOnlyFixedHeadersAndOrigin()
    {
        using var db = ModelContext();
        var inputs = new Inputs();
        var current = inputs.Current(inputs.Service(db));
        inputs.Gemini.ApiKey = "later-shared-change";
        inputs.Gemini.Model = "later-shared-model";
        await current.ResolveAsync(null);
        Assert.Equal("gemini-server-model", current.Model);
        using var probe = current.CreateConnectionProbeRequest();
        Assert.Equal(HttpMethod.Post, probe.Method);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-server-model:streamGenerateContent?alt=sse", probe.RequestUri!.AbsoluteUri);
        Assert.Equal("server-gemini-key", Assert.Single(probe.Headers.GetValues("x-goog-api-key")));
        Assert.Equal("?alt=sse", probe.RequestUri.Query);
        await current.ResolveAsync(null);
        Assert.Equal("gemini-server-model", current.Model);
        await Assert.ThrowsAsync<InvalidOperationException>(() => current.ResolveAsync("another-user"));
    }

    [SqlFact]
    public async Task StoresOnlyProtectedKeysAndPreservesTheOtherProvider()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var user = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var input = new Inputs();
        var service = input.Service(db);
        await service.SaveAsync(user.Id, "Gemini", GeminiKey, "gemini-user-model");
        await service.SaveAsync(user.Id, "OpenAI", OpenAiKey, "gpt-user-model");
        db.ChangeTracker.Clear();
        var row = await db.UserAiSettings.SingleAsync();
        Assert.NotEqual(GeminiKey, row.ProtectedGeminiApiKey);
        Assert.NotEqual(OpenAiKey, row.ProtectedOpenAiApiKey);
        Assert.DoesNotContain(GeminiKey, row.ProtectedGeminiApiKey!);
        Assert.DoesNotContain(OpenAiKey, row.ProtectedOpenAiApiKey!);
        var state = await service.GetAsync(user.Id);
        Assert.True(state.HasGeminiKey && state.HasOpenAiKey);
        Assert.Equal("OpenAI", state.Provider);
        Assert.Equal("gemini-user-model", state.GeminiModel);
        Assert.Equal("gpt-user-model", state.OpenAiModel);
        var safeJson = JsonSerializer.Serialize(state);
        Assert.DoesNotContain(GeminiKey, safeJson);
        Assert.DoesNotContain(OpenAiKey, safeJson);
        Assert.DoesNotContain(row.ProtectedGeminiApiKey!, safeJson);
        await service.SaveAsync(user.Id, "OpenAI", "  ", "gpt-updated-model");
        var current = input.Current(service);
        await current.ResolveAsync(user.Id);
        using var request = current.CreateConnectionProbeRequest();
        Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
        Assert.Equal(OpenAiKey, request.Headers.Authorization!.Parameter);
        using var probeBody = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        Assert.Equal("gpt-updated-model", probeBody.RootElement.GetProperty("model").GetString());
    }

    [SqlFact]
    public async Task CopyingCiphertextAcrossAccountsOrProvidersCannotUseTheKey()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var first = await sql.CreateStudentAsync();
        var second = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var input = new Inputs();
        var service = input.Service(db);
        await service.SaveAsync(first.Id, "Gemini", GeminiKey, "gemini-user-model");
        var firstRow = await db.UserAiSettings.SingleAsync();
        firstRow.ProtectedOpenAiApiKey = firstRow.ProtectedGeminiApiKey;
        db.UserAiSettings.Add(new UserAiSettings
        {
            UserId = second.Id, UsePersonalSettings = true, Provider = "Gemini",
            ProtectedGeminiApiKey = firstRow.ProtectedGeminiApiKey
        });
        await db.SaveChangesAsync();
        var firstState = await service.GetAsync(first.Id);
        Assert.True(firstState.HasGeminiKey);
        Assert.False(firstState.HasOpenAiKey);
        Assert.True(firstState.OpenAiKeyUnavailable);
        var secondState = await service.GetAsync(second.Id);
        Assert.False(secondState.HasGeminiKey);
        Assert.True(secondState.GeminiKeyUnavailable);
        var current = input.Current(service);
        await current.ResolveAsync(second.Id);
        Assert.True(current.UsePersonalSettings);
        Assert.False(current.IsConfigured);
        Assert.Throws<UserAiSettingsValidationException>(() => current.CreateConnectionProbeRequest());
        await service.SaveAsync(second.Id, "Gemini", "replacement-synthetic-key", "gemini-new-model");
        Assert.True((await service.GetAsync(second.Id)).HasGeminiKey);
    }

    [SqlFact]
    public async Task DeletingSelectedKeyNeverUsesServerUntilExplicitlySelected()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var user = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var input = new Inputs();
        var service = input.Service(db);
        await service.SaveAsync(user.Id, "OpenAI", OpenAiKey, "gpt-user-model");
        await service.DeleteKeyAsync(user.Id, "OpenAI");
        var current = input.Current(service);
        await current.ResolveAsync(user.Id);
        Assert.True(current.UsePersonalSettings);
        Assert.Equal("OpenAI", current.Provider);
        Assert.False(current.IsConfigured);
        var failure = await Assert.ThrowsAsync<UserAiSettingsValidationException>(() => service.SaveAsync(user.Id, "OpenAI", "", "gpt-user-model"));
        Assert.Equal("ApiKey", failure.Field);
        await service.UseServerDefaultsAsync(user.Id);
        var server = input.Current(service);
        await server.ResolveAsync(user.Id);
        Assert.False(server.UsePersonalSettings);
        Assert.True(server.IsConfigured);
        Assert.Equal("Gemini", server.Provider);
    }

    [SqlFact]
    public async Task SeparateUsersAndExistingRequestsKeepIsolatedOptions()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var first = await sql.CreateStudentAsync();
        var second = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var input = new Inputs();
        var service = input.Service(db);
        await service.SaveAsync(first.Id, "OpenAI", OpenAiKey, "gpt-user-model");
        await service.SaveAsync(second.Id, "Gemini", GeminiKey, "gemini-user-model");
        var firstRequest = input.Current(service);
        var secondRequest = input.Current(service);
        await firstRequest.ResolveAsync(first.Id);
        await secondRequest.ResolveAsync(second.Id);
        await service.SaveAsync(first.Id, "Gemini", "different-synthetic-key", "gemini-changed-model");
        // Exception-handler pipeline re-execution keeps this request's original user and snapshot.
        await firstRequest.ResolveAsync(first.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => firstRequest.ResolveAsync(second.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => firstRequest.ResolveAsync(null));
        using var firstProbe = firstRequest.CreateConnectionProbeRequest();
        using var secondProbe = secondRequest.CreateConnectionProbeRequest();
        Assert.Equal(OpenAiKey, firstProbe.Headers.Authorization!.Parameter);
        Assert.Equal(GeminiKey, Assert.Single(secondProbe.Headers.GetValues("x-goog-api-key")));
        Assert.Equal("OpenAI", firstRequest.Provider);
        Assert.Equal("gpt-user-model", firstRequest.Model);
        Assert.Equal("server-openai-key", input.OpenAi.ApiKey);
        Assert.Equal("server-gemini-key", input.Gemini.ApiKey);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options;
        using var clients = new NoNetworkClients();
        var firstAi = firstRequest.CreateService(clients, options, new AiRequestLimiter());
        var secondAi = secondRequest.CreateService(clients, options, new AiRequestLimiter());
        Assert.Equal("OpenAI", firstAi.Provider);
        Assert.Equal("gpt-user-model", firstAi.Model);
        Assert.Equal("Gemini", secondAi.Provider);
        Assert.Equal("gemini-user-model", secondAi.Model);
        Assert.True(firstAi.IsConfigured && secondAi.IsConfigured);
    }

    [SqlFact]
    public async Task MissingKeyRingFailsClosedAndUserCanReplaceKey()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var user = await sql.CreateStudentAsync();
        await using var db = sql.Context();
        var original = new Inputs();
        await original.Service(db).SaveAsync(user.Id, "Gemini", GeminiKey, "gemini-user-model");
        var freshKeyRing = new Inputs();
        var service = freshKeyRing.Service(db);
        Assert.True((await service.GetAsync(user.Id)).GeminiKeyUnavailable);
        var request = freshKeyRing.Current(service);
        await request.ResolveAsync(user.Id);
        Assert.False(request.IsConfigured);
        await service.SaveAsync(user.Id, "Gemini", GeminiKey, "gemini-user-model");
        Assert.True((await service.GetAsync(user.Id)).HasGeminiKey);
    }

    [SqlFact]
    public async Task ConcurrentUpdatesReturnSafeConflictAndDoNotOverwriteLatestKey()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var user = await sql.CreateStudentAsync();
        var input = new Inputs();
        await using var firstDb = sql.Context();
        await input.Service(firstDb).SaveAsync(user.Id, "Gemini", GeminiKey, "gemini-user-model");
        await using var secondDb = sql.Context();
        await secondDb.UserAiSettings.SingleAsync();
        await input.Service(firstDb).SaveAsync(user.Id, "Gemini", "latest-synthetic-key", "gemini-user-model");
        var conflict = await Assert.ThrowsAsync<UserAiSettingsValidationException>(() =>
            input.Service(secondDb).SaveAsync(user.Id, "Gemini", "stale-synthetic-key", "gemini-user-model"));
        Assert.Equal("", conflict.Field);
        Assert.DoesNotContain("synthetic", conflict.Message);
        var current = input.Current(input.Service(firstDb));
        await current.ResolveAsync(user.Id);
        using var probe = current.CreateConnectionProbeRequest();
        Assert.Equal("latest-synthetic-key", Assert.Single(probe.Headers.GetValues("x-goog-api-key")));
    }

    [SqlFact]
    public async Task DeletingUserAlsoDeletesOnlyThatUsersSettings()
    {
        await using var sql = await SqlIntegrationTests.SqlDatabase.CreateAsync();
        var first = await sql.CreateStudentAsync();
        var second = await sql.CreateStudentAsync();
        var input = new Inputs();
        await using var db = sql.Context();
        var service = input.Service(db);
        await service.SaveAsync(first.Id, "Gemini", GeminiKey, "gemini-user-model");
        await service.SaveAsync(second.Id, "OpenAI", OpenAiKey, "gpt-user-model");
        db.ChangeTracker.Clear();
        await db.Users.Where(x => x.Id == first.Id).ExecuteDeleteAsync();
        var remaining = Assert.Single(await db.UserAiSettings.ToListAsync());
        Assert.Equal(second.Id, remaining.UserId);
    }

    private static AppDbContext ModelContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ModelMetadataOnly;Trusted_Connection=True;").Options);

    private sealed class Inputs
    {
        private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
        private readonly AiProviderOptions provider = new() { Provider = "Gemini" };
        public GeminiOptions Gemini { get; } = new() { ApiKey = "server-gemini-key", Model = "gemini-server-model" };
        public OpenAiOptions OpenAi { get; } = new() { ApiKey = "server-openai-key", Model = "gpt-server-model" };
        public UserAiSettingsService Service(AppDbContext db) => new(db, protection, Options.Create(provider),
            Options.Create(Gemini), Options.Create(OpenAi), TimeProvider.System);
        public CurrentUserAiConfiguration Current(UserAiSettingsService service) => new(service, Options.Create(provider),
            Options.Create(Gemini), Options.Create(OpenAi));
    }

    private sealed class NoNetworkClients : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient client = new(new RejectNetwork());
        public HttpClient CreateClient(string name) => client;
        public void Dispose() => client.Dispose();
        private sealed class RejectNetwork : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                throw new InvalidOperationException("This test must never make an AI request.");
        }
    }
}
