using TaskFlow.TestInfrastructure.Api;
using TaskFlow.TestInfrastructure.Database;
using Xunit;

namespace TaskFlow.TestInfrastructure.Fixtures;

/// <summary>
/// Shared, expensive state for the integration-test assembly: one SQL Server
/// container and one application host.
/// </summary>
/// <remarks>
/// Created once per test run. Individual tests get isolation from
/// <see cref="SqlServerContainerFixture.ResetAsync"/>, not from a fresh container.
/// </remarks>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    private TaskFlowApiFactory? _api;

    /// <summary>The disposable SQL Server instance.</summary>
    public SqlServerContainerFixture Database { get; } = new();

    /// <summary>The application under test.</summary>
    public TaskFlowApiFactory Api =>
        _api ?? throw new InvalidOperationException("The fixture has not been initialised.");

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await Database.InitializeAsync();
        _api = new TaskFlowApiFactory(Database.ConnectionString);

        // Force host construction now, so a startup failure is reported by the
        // fixture rather than surfacing as an unrelated failure in the first test.
        _ = Api.Services;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        // Dispose in reverse order of creation: the host holds pooled connections to
        // the database, so it must go first. Both steps run even if one throws, so a
        // failing host can never leak the container.
        try
        {
            if (_api is not null)
            {
                await _api.DisposeAsync();
            }
        }
        finally
        {
            await Database.DisposeAsync();
        }
    }
}
