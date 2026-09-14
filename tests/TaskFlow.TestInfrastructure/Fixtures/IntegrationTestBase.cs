using TaskFlow.Core.Entities;
using TaskFlow.Infrastructure.Data;
using TaskFlow.TestInfrastructure.Api;
using TaskFlow.TestInfrastructure.TestData;
using Xunit;

namespace TaskFlow.TestInfrastructure.Fixtures;

/// <summary>
/// Base class for integration tests. Guarantees each test starts from an empty
/// database and supplies the helpers tests need most.
/// </summary>
/// <remarks>
/// The goal is that a derived test reads as arrange/act/assert about behaviour,
/// with no container, connection-string or authentication plumbing in view.
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly IntegrationTestFixture _fixture;

    /// <summary>Creates the base. xUnit supplies the shared fixture.</summary>
    protected IntegrationTestBase(IntegrationTestFixture fixture) => _fixture = fixture;

    /// <summary>The application under test.</summary>
    protected TaskFlowApiFactory Api => _fixture.Api;

    /// <summary>An unauthenticated client.</summary>
    protected HttpClient Client { get; private set; } = null!;

    /// <summary>Resets the database and clears diagnostics before every test.</summary>
    public async Task InitializeAsync()
    {
        await _fixture.Database.ResetAsync();
        Api.ClearDiagnostics();
        Client = Api.CreateApiClient();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Returns a client authenticated as <paramref name="user"/>.</summary>
    protected Task<HttpClient> ClientForAsync(TestUser user) =>
        Api.CreateAuthenticatedClientAsync(user);

    /// <summary>Seeds <paramref name="user"/> and returns the persisted entity.</summary>
    protected Task<User> SeedUserAsync(TestUser user) => Api.EnsureUserExistsAsync(user);

    /// <summary>Persists <paramref name="entities"/> and returns them with database-assigned keys.</summary>
    protected async Task<TEntity[]> SeedAsync<TEntity>(params TEntity[] entities)
        where TEntity : class
    {
        await Api.ExecuteDbContextAsync(async context =>
        {
            context.Set<TEntity>().AddRange(entities);
            await context.SaveChangesAsync();
        });

        return entities;
    }

    /// <summary>Reads persisted state, to assert what a request actually wrote.</summary>
    protected Task<TResult> QueryAsync<TResult>(Func<ApplicationDbContext, Task<TResult>> query) =>
        Api.ExecuteDbContextAsync(query);

    /// <summary>
    /// Diagnostic context for a failure: the HTTP traffic and the application log.
    /// </summary>
    /// <remarks>Both are redacted; neither contains tokens or passwords.</remarks>
    protected string Diagnostics() =>
        $"""

        ===== HTTP exchanges =====
        {Api.HttpRecorder.Render()}
        ===== application log =====
        {Api.LogSink.Render()}
        """;
}
