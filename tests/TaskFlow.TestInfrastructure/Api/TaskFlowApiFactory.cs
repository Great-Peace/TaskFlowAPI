using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;
using Serilog.Extensions.Logging;
using TaskFlow.Core.DTOs;
using TaskFlow.Core.Entities;
using TaskFlow.Infrastructure.Data;
using TaskFlow.TestInfrastructure.Builders;
using TaskFlow.TestInfrastructure.Http;
using TaskFlow.TestInfrastructure.Logging;
using TaskFlow.TestInfrastructure.TestData;

namespace TaskFlow.TestInfrastructure.Api;

/// <summary>
/// Boots the real TaskFlow API in-process, wired to a disposable SQL Server.
/// </summary>
/// <remarks>
/// <para>
/// Everything the application registers in <c>Program.cs</c> is used unchanged:
/// the real DI container, the real middleware pipeline, the real controllers, the
/// real EF Core persistence layer. Only two things are redirected - the connection
/// string, which points at the test container, and the JWT signing settings, which
/// use a test-only key so the suite never depends on a committed secret.
/// </para>
/// <para>
/// The connection string is overridden through configuration rather than by removing
/// and re-registering <c>DbContextOptions</c>. That keeps the application's own
/// registration code on the tested path instead of substituting a parallel one.
/// </para>
/// </remarks>
public sealed class TaskFlowApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Signing key used only by tests. HS256 requires at least 32 bytes.
    /// This is a fixture value, not a credential: it is never configured in any
    /// environment the application is deployed to.
    /// </summary>
    private const string TestSigningKey = "test-only-signing-key-do-not-use-in-any-real-environment";

    private const string TestIssuer = "TaskFlowAPI.Tests";
    private const string TestAudience = "TaskFlowAPI.Tests.Clients";

    private readonly string _connectionString;

    /// <summary>Creates the factory.</summary>
    /// <param name="connectionString">Connection string of the disposable SQL Server database.</param>
    public TaskFlowApiFactory(string connectionString) => _connectionString = connectionString;

    /// <summary>Application log events captured during the current test.</summary>
    public InMemoryLogSink LogSink { get; } = new();

    /// <summary>HTTP traffic recorded during the current test, with secrets redacted.</summary>
    public HttpExchangeRecorder HttpRecorder { get; } = new();

    /// <summary>
    /// Controllable clock injected into the application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Frozen: it does not advance unless a test advances it. Any behaviour derived
    /// from "now" - issued timestamps, token expiry, overdue calculations - therefore
    /// produces the same value every time it is read within a test.
    /// </para>
    /// <para>
    /// Anchored to the real current time rather than a hardcoded date. The JWT bearer
    /// middleware validates token lifetime against the system clock, which cannot be
    /// substituted through <c>TokenValidationParameters</c> here. A clock fixed to a
    /// past date would issue tokens that the middleware considers already expired, and
    /// every authenticated test would fail with a 401. Anchoring to now keeps tokens
    /// valid while still being frozen, so assertions compare against
    /// <c>Clock.GetUtcNow()</c> rather than against a literal date.
    /// </para>
    /// <para>
    /// The consequence is that token expiry cannot be driven past its limit through
    /// HTTP. That behaviour is asserted at the unit level instead, where the expiry
    /// claim is inspected directly.
    /// </para>
    /// </remarks>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>Creates a client that records its traffic and does not follow redirects.</summary>
    /// <remarks>
    /// Redirects are not followed so that a misconfigured HTTPS redirection surfaces
    /// as a visible 307 rather than silently changing what the test measures.
    /// </remarks>
    public HttpClient CreateApiClient()
    {
        var client = CreateDefaultClient(new RecordingHttpHandler(HttpRecorder));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>
    /// Seeds <paramref name="user"/> and returns a client carrying a genuine bearer
    /// token for that identity.
    /// </summary>
    /// <remarks>
    /// The token is obtained by calling the real login endpoint rather than by
    /// forging a token or stubbing authentication. That keeps the authentication
    /// path under test: if login breaks, these tests fail too, which is correct.
    /// Seeding directly is what makes an Admin client possible at all, since the
    /// register endpoint hardcodes the User role.
    /// </remarks>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(TestUser user)
    {
        await EnsureUserExistsAsync(user);

        var client = CreateApiClient();
        var token = await AuthenticateAsync(client, user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Seeds <paramref name="user"/> if absent and returns the persisted entity.</summary>
    public async Task<User> EnsureUserExistsAsync(TestUser user)
    {
        return await ExecuteDbContextAsync(async context =>
        {
            var existing = context.Users.FirstOrDefault(u => u.Email == user.Email);
            if (existing is not null)
            {
                return existing;
            }

            var entity = UserBuilder.From(user).Build();
            context.Users.Add(entity);
            await context.SaveChangesAsync();
            return entity;
        });
    }

    /// <summary>Logs in through the public endpoint and returns the bearer token.</summary>
    public static async Task<string> AuthenticateAsync(HttpClient client, TestUser user)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginDto { Email = user.Email, Password = user.Password });

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Could not authenticate test user {user.Email}. " +
                $"Login returned {(int)response.StatusCode}: {HttpExchangeRecorder.Redact(body)}");
        }

        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        return auth?.Token ?? throw new InvalidOperationException("Login succeeded but returned no token.");
    }

    /// <summary>Runs <paramref name="action"/> against a fresh scoped DbContext.</summary>
    /// <remarks>
    /// Used to arrange state before a request and to assert persisted state after one.
    /// A new scope each time avoids reading a stale entity out of a cached change tracker.
    /// </remarks>
    public async Task ExecuteDbContextAsync(Func<ApplicationDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await action(context);
    }

    /// <inheritdoc cref="ExecuteDbContextAsync(Func{ApplicationDbContext, Task})" />
    public async Task<TResult> ExecuteDbContextAsync<TResult>(Func<ApplicationDbContext, Task<TResult>> action)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await action(context);
    }

    /// <summary>Clears captured diagnostics so output belongs to a single test.</summary>
    public void ClearDiagnostics()
    {
        LogSink.Clear();
        HttpRecorder.Clear();
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" rather than "Development": this keeps the application's
        // Development-only EnsureCreated() out of the way, because schema creation is
        // owned by SqlServerContainerFixture.
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["JwtSettings:SecretKey"] = TestSigningKey,
                ["JwtSettings:Issuer"] = TestIssuer,
                ["JwtSettings:Audience"] = TestAudience,
                ["JwtSettings:ExpiryInHours"] = "24"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Deterministic clock. Registered as the framework abstraction so that
            // production code depends on TimeProvider, not on a test type.
            services.AddSingleton<TimeProvider>(Clock);

            // Redirect application logging into the in-memory sink.
            //
            // Program.cs configures Serilog to write to the console and to rolling
            // files. Left alone, a test run would spray the application's logs over
            // the test output and drop log files into the test binary directory.
            // Replacing ILoggerFactory outright is deterministic: ConfigureTestServices
            // runs after the application's own registrations, so this is the instance
            // that gets resolved. Reassigning the static Log.Logger instead is not
            // reliable, because the application's logger has already been captured by
            // the time the host finishes starting.
            services.AddSingleton<ILoggerFactory>(_ => new SerilogLoggerFactory(
                new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Sink(LogSink)
                    .CreateLogger(),
                dispose: true));
        });
    }
}
