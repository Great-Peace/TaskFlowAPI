using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Core.DTOs;
using TaskFlow.TestInfrastructure.Builders;
using TaskFlow.TestInfrastructure.Fixtures;
using TaskFlow.TestInfrastructure.Http;
using TaskFlow.TestInfrastructure.TestData;

namespace TaskFlow.IntegrationTests;

/// <summary>
/// Proves the test framework itself works before any behaviour is asserted with it.
/// </summary>
/// <remarks>
/// If these fail, every other integration test result is meaningless. They verify
/// that the container starts, the real application boots against it, authentication
/// produces a usable token, state actually reaches SQL Server, and the per-test
/// reset genuinely isolates tests from one another.
/// </remarks>
public class TestInfrastructureSmokeTests : IntegrationTestBase
{
    public TestInfrastructureSmokeTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Application_starts_and_serves_requests()
    {
        var response = await Client.GetAsync("/api/projects");

        // Unauthenticated: proves routing, the auth middleware and the pipeline are live.
        await ApiAssert.StatusAsync(response, HttpStatusCode.Unauthorized, Diagnostics());
    }

    [Fact]
    public async Task Registering_a_user_persists_it_to_sql_server()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            FirstName = "Smoke",
            LastName = "Test",
            Email = "smoke@taskflow.test",
            Password = TestUsers.DefaultPassword,
            ConfirmPassword = TestUsers.DefaultPassword
        });

        var body = await ApiAssert.StatusAndContentAsync<AuthResponseDto>(
            response, HttpStatusCode.Created, Diagnostics());

        Assert.NotEmpty(body.Token);

        // The request is only genuinely complete if the row is in the database.
        var persisted = await QueryAsync(db =>
            db.Users.SingleOrDefaultAsync(u => u.Email == "smoke@taskflow.test"));

        Assert.NotNull(persisted);
        Assert.Equal("Smoke", persisted!.FirstName);
        Assert.NotEqual(TestUsers.DefaultPassword, persisted.PasswordHash);
    }

    [Fact]
    public async Task Seeded_user_can_authenticate_through_the_real_login_endpoint()
    {
        var client = await ClientForAsync(TestUsers.Alice);

        var response = await client.GetAsync("/api/projects");

        await ApiAssert.StatusAsync(response, HttpStatusCode.OK, Diagnostics());
    }

    [Fact]
    public async Task An_admin_client_can_be_created_even_though_registration_cannot_grant_the_role()
    {
        await ClientForAsync(TestUsers.Admin);

        var persisted = await QueryAsync(db =>
            db.Users.SingleAsync(u => u.Email == TestUsers.Admin.Email));

        Assert.Equal(TestRoles.Admin, persisted.Role);
    }

    // The next two tests are a pair. Each seeds a project and asserts it is the only
    // one present. They pass together only if the database is reset between tests.

    [Fact]
    public async Task Database_is_isolated_between_tests_first()
    {
        var user = await SeedUserAsync(TestUsers.Alice);
        await SeedAsync(new ProjectBuilder().WithName("First test project").OwnedBy(user).Build());

        var names = await QueryAsync(db => db.Projects.Select(p => p.Name).ToListAsync());

        Assert.Equal(new[] { "First test project" }, names);
    }

    [Fact]
    public async Task Database_is_isolated_between_tests_second()
    {
        var user = await SeedUserAsync(TestUsers.Bob);
        await SeedAsync(new ProjectBuilder().WithName("Second test project").OwnedBy(user).Build());

        var names = await QueryAsync(db => db.Projects.Select(p => p.Name).ToListAsync());

        Assert.Equal(new[] { "Second test project" }, names);
    }

    [Fact]
    public async Task Application_logs_are_captured_for_failure_diagnostics()
    {
        await Client.GetAsync("/api/projects");

        var log = Api.LogSink.Render();

        Assert.DoesNotContain("no application log events captured", log);
    }

    [Fact]
    public async Task Recorded_http_traffic_redacts_credentials()
    {
        await Client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            FirstName = "Redaction",
            LastName = "Check",
            Email = "redaction@taskflow.test",
            Password = TestUsers.DefaultPassword,
            ConfirmPassword = TestUsers.DefaultPassword
        });

        var recorded = Api.HttpRecorder.Render();

        // The password went out and a JWT came back; neither may appear in diagnostics.
        Assert.DoesNotContain(TestUsers.DefaultPassword, recorded);
        Assert.DoesNotContain("eyJ", recorded);
        Assert.Contains("REDACTED", recorded);
    }
}
