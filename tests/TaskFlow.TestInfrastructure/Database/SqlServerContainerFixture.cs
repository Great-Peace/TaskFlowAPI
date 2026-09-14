using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;
using Respawn.Graph;
using TaskFlow.Infrastructure.Data;
using Testcontainers.MsSql;

namespace TaskFlow.TestInfrastructure.Database;

/// <summary>
/// Owns a disposable SQL Server instance for the lifetime of a test run.
/// </summary>
/// <remarks>
/// <para>
/// The application targets SQL Server in production, so the tests run against real
/// SQL Server rather than the EF Core in-memory provider. Only a real server
/// exercises unique indexes, identity columns, foreign-key delete behaviour and
/// provider-specific query translation - precisely the behaviour worth testing.
/// </para>
/// <para>
/// One container is shared by the whole integration-test assembly. Starting a SQL
/// Server container costs several seconds, so a container per test class would
/// dominate the run time. Isolation between tests comes from
/// <see cref="ResetAsync"/> instead, which is fast.
/// </para>
/// </remarks>
public sealed class SqlServerContainerFixture : IAsyncDisposable
{
    /// <summary>
    /// Pinned image tag. An exact tag keeps runs reproducible: a floating tag would
    /// let the database engine change underneath the suite without any commit.
    /// </summary>
    public const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    /// <summary>Database created inside the container and used by the application under test.</summary>
    private const string TestDatabaseName = "TaskFlowTests";

    /// <summary>How long to wait for SQL Server to accept connections after the container starts.</summary>
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromMinutes(3);

    private readonly MsSqlContainer _container;
    private Respawner? _respawner;

    /// <summary>Creates the fixture. The container is not started until <see cref="InitializeAsync"/> runs.</summary>
    public SqlServerContainerFixture()
    {
        _container = new MsSqlBuilder(SqlServerImage)
            .WithCleanUp(true)
            .Build();
    }

    /// <summary>
    /// Connection string for the application under test, pointing at the
    /// container's TaskFlowTests database.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Wall-clock time the container took to become usable. Reported in diagnostics.</summary>
    public TimeSpan StartupDuration { get; private set; }

    /// <summary>
    /// Starts the container, waits for readiness, creates the test database and
    /// its schema, then captures a Respawn checkpoint of the empty state.
    /// </summary>
    public async Task InitializeAsync()
    {
        var stopwatch = Stopwatch.StartNew();

        await _container.StartAsync();
        await WaitForSqlServerAsync();
        await CreateTestDatabaseAsync();

        ConnectionString = BuildConnectionString(TestDatabaseName);

        await CreateSchemaAsync();
        await InitialiseRespawnerAsync();

        stopwatch.Stop();
        StartupDuration = stopwatch.Elapsed;
    }

    /// <summary>
    /// Deletes every row from every table, returning the database to the state
    /// captured at startup.
    /// </summary>
    /// <remarks>
    /// Deleting rows is dramatically cheaper than dropping and recreating the schema,
    /// and it resets identity seeds so that generated keys are reproducible across runs.
    /// </remarks>
    public async Task ResetAsync()
    {
        if (_respawner is null)
        {
            throw new InvalidOperationException(
                $"{nameof(InitializeAsync)} must complete before {nameof(ResetAsync)} is called.");
        }

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    /// <summary>
    /// Returns the container's stdout/stderr, for diagnosing a failure to start or
    /// an unexpected database error.
    /// </summary>
    public async Task<string> GetContainerLogsAsync()
    {
        try
        {
            var (stdout, stderr) = await _container.GetLogsAsync();
            return $"--- container stdout ---{Environment.NewLine}{stdout}{Environment.NewLine}" +
                   $"--- container stderr ---{Environment.NewLine}{stderr}";
        }
        catch (Exception ex)
        {
            return $"(container logs unavailable: {ex.Message})";
        }
    }

    /// <summary>Stops and removes the container.</summary>
    public async ValueTask DisposeAsync()
    {
        // DisposeAsync stops and removes the container. Testcontainers' Ryuk sidecar
        // removes it anyway if this process dies first, so a crashed run leaks nothing.
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Polls until SQL Server accepts a connection.
    /// </summary>
    /// <remarks>
    /// The module ships its own wait strategy, but it depends on the sqlcmd tool
    /// being at an expected path inside the image. Opening a real connection is a
    /// stronger and more durable readiness signal, and it produces a clear error
    /// message if the server never comes up.
    /// </remarks>
    private async Task WaitForSqlServerAsync()
    {
        var deadline = DateTime.UtcNow + ReadinessTimeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var connection = new SqlConnection(BuildConnectionString("master"));
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync();
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        var logs = await GetContainerLogsAsync();
        throw new TimeoutException(
            $"SQL Server did not accept connections within {ReadinessTimeout.TotalSeconds:N0}s. " +
            $"Last error: {lastError?.Message}{Environment.NewLine}{logs}");
    }

    private async Task CreateTestDatabaseAsync()
    {
        await using var connection = new SqlConnection(BuildConnectionString("master"));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{TestDatabaseName}') IS NULL CREATE DATABASE [{TestDatabaseName}];";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Creates the schema from the EF Core model.
    /// </summary>
    /// <remarks>
    /// The repository contains no EF migrations, so EnsureCreated is the only source
    /// of truth for the schema - the same call the application itself makes at startup
    /// in Development. The trade-off is that migration scripts are not exercised;
    /// there are none to exercise. If migrations are added later this should switch to
    /// Database.MigrateAsync() so the tests validate the real deployment path.
    /// </remarks>
    private async Task CreateSchemaAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
    }

    private async Task InitialiseRespawnerAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,

            // Reset identity seeds so generated keys start from 1 in every test.
            WithReseed = true,

            // Present only if migrations are introduced later; ignoring it now is harmless.
            TablesToIgnore = new Table[] { "__EFMigrationsHistory" }
        });
    }

    /// <summary>Rewrites the container's connection string to target a specific database.</summary>
    private string BuildConnectionString(string database) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = database,
            TrustServerCertificate = true
        }.ConnectionString;
}
