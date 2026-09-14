using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Core.DTOs;
using TaskFlow.TestInfrastructure.Fixtures;
using TaskFlow.TestInfrastructure.Http;
using TaskFlow.TestInfrastructure.TestData;

namespace TaskFlow.IntegrationTests.EndToEnd;

/// <summary>
/// A complete user journey through the service, entered only through HTTP.
/// </summary>
/// <remarks>
/// <para>
/// The integration tests each verify one behaviour in isolation, from a freshly
/// reset database. This does something different: it follows a single user from
/// having no account to having deleted their work, carrying real state - a bearer
/// token, a generated project id - from one step to the next.
/// </para>
/// <para>
/// Nothing here reaches into the application. There are no service resolutions and
/// no repository calls; every step is a request a real client could make, and the id
/// used in later steps is the one the API returned earlier. That is what makes this
/// a test of routing, model binding, validation, authentication, authorization,
/// business logic, persistence and serialisation working together, rather than of
/// each in isolation.
/// </para>
/// <para>
/// The database is consulted only at the very end, to confirm that what the API
/// reported actually reached storage.
/// </para>
/// </remarks>
public class ProjectLifecycleWorkflowTests : IntegrationTestBase
{
    public ProjectLifecycleWorkflowTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task A_user_can_register_create_read_update_and_delete_a_project()
    {
        using var anonymous = Api.CreateApiClient();

        // 1. A protected resource is refused before there is any identity at all.
        var beforeRegistering = await anonymous.GetAsync("/api/projects");
        await ApiAssert.StatusAsync(beforeRegistering, HttpStatusCode.Unauthorized, Diagnostics());

        // 2. Register. The API returns a usable token straight away.
        var registration = await anonymous.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            FirstName = "Mia",
            LastName = "Okafor",
            Email = "mia.okafor@taskflow.test",
            Password = "Workflow-Passw0rd!",
            ConfirmPassword = "Workflow-Passw0rd!"
        });

        var registered = await ApiAssert.StatusAndContentAsync<AuthResponseDto>(
            registration, HttpStatusCode.Created, Diagnostics());
        Assert.Equal(TestRoles.User, registered.Role);

        // 3. Log in separately, to prove the stored credentials work rather than
        //    relying on the token registration happened to hand back.
        var login = await anonymous.PostAsJsonAsync("/api/auth/login", new LoginDto
        {
            Email = "mia.okafor@taskflow.test",
            Password = "Workflow-Passw0rd!"
        });

        var session = await ApiAssert.StatusAndContentAsync<AuthResponseDto>(
            login, HttpStatusCode.OK, Diagnostics());
        Assert.Equal(registered.UserId, session.UserId);

        using var mia = Api.CreateApiClient();
        mia.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);

        // 4. The account starts with nothing.
        var initialList = await mia.GetAsync("/api/projects");
        var empty = await ApiAssert.StatusAndContentAsync<List<ProjectDto>>(
            initialList, HttpStatusCode.OK, Diagnostics());
        Assert.Empty(empty);

        // 5. Create a project.
        var creation = await mia.PostAsJsonAsync("/api/projects", new CreateProjectDto
        {
            Name = "Quarterly migration",
            Description = "Move the reporting pipeline",
            Status = "Planning",
            StartDate = new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        var created = await ApiAssert.StatusAndContentAsync<ProjectDto>(
            creation, HttpStatusCode.Created, Diagnostics());

        Assert.True(created.Id > 0);
        Assert.Equal("Quarterly migration", created.Name);
        Assert.Equal(registered.UserId, created.CreatedById);
        Assert.Equal("Mia Okafor", created.CreatedByName);

        // 6. Read it back by following the Location header the API returned.
        var location = creation.Headers.Location;
        Assert.NotNull(location);

        var retrieval = await mia.GetAsync(location);
        var retrieved = await ApiAssert.StatusAndContentAsync<ProjectDto>(
            retrieval, HttpStatusCode.OK, Diagnostics());

        Assert.Equal(created.Id, retrieved.Id);
        Assert.Equal("Planning", retrieved.Status);
        Assert.Empty(retrieved.Tasks);

        // 7. It now appears in the owner's list.
        var listed = await ApiAssert.StatusAndContentAsync<List<ProjectDto>>(
            await mia.GetAsync("/api/projects"), HttpStatusCode.OK, Diagnostics());
        Assert.Single(listed);
        Assert.Equal(created.Id, listed[0].Id);

        // 8. Update it. Only Status is sent, so everything else must survive.
        var update = await mia.PutAsJsonAsync($"/api/projects/{created.Id}",
            new UpdateProjectDto { Status = "Active" });

        var updated = await ApiAssert.StatusAndContentAsync<ProjectDto>(
            update, HttpStatusCode.OK, Diagnostics());

        Assert.Equal("Active", updated.Status);
        Assert.Equal("Quarterly migration", updated.Name);
        Assert.Equal("Move the reporting pipeline", updated.Description);
        Assert.Equal(new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc), updated.StartDate);

        // 9. The authorization boundary: a second, unrelated user must not be able to
        //    see, change or destroy this project, and must not learn that it exists.
        await AssertAnotherUserCannotTouch(created.Id);

        // 10. An administrator, by contrast, can see it in the oversight view.
        var adminClient = await ClientForAsync(TestUsers.Admin);
        var allProjects = await ApiAssert.StatusAndContentAsync<List<ProjectDto>>(
            await adminClient.GetAsync("/api/projects/all"), HttpStatusCode.OK, Diagnostics());
        Assert.Contains(allProjects, p => p.Id == created.Id);

        // 11. Delete it.
        var deletion = await mia.DeleteAsync($"/api/projects/{created.Id}");
        await ApiAssert.StatusAsync(deletion, HttpStatusCode.NoContent, Diagnostics());

        // 12. It is gone from both the single-resource route and the list.
        await ApiAssert.StatusAsync(
            await mia.GetAsync($"/api/projects/{created.Id}"), HttpStatusCode.NotFound, Diagnostics());

        var finalList = await ApiAssert.StatusAndContentAsync<List<ProjectDto>>(
            await mia.GetAsync("/api/projects"), HttpStatusCode.OK, Diagnostics());
        Assert.Empty(finalList);

        // 13. Finally, confirm the database agrees with what the API reported. The
        //     user survives; the project does not.
        var remainingProjects = await QueryAsync(db => db.Projects.CountAsync());
        var userStillExists = await QueryAsync(db =>
            db.Users.AnyAsync(u => u.Email == "mia.okafor@taskflow.test"));

        Assert.Equal(0, remainingProjects);
        Assert.True(userStillExists);
    }

    /// <summary>
    /// Verifies that a different authenticated user is refused on every route that
    /// addresses the project, and cannot tell it apart from one that never existed.
    /// </summary>
    private async Task AssertAnotherUserCannotTouch(int projectId)
    {
        var intruder = await ClientForAsync(TestUsers.Bob);

        var read = await intruder.GetAsync($"/api/projects/{projectId}");
        var write = await intruder.PutAsJsonAsync($"/api/projects/{projectId}",
            new UpdateProjectDto { Name = "Taken over" });
        var destroy = await intruder.DeleteAsync($"/api/projects/{projectId}");

        await ApiAssert.StatusAsync(read, HttpStatusCode.NotFound, Diagnostics());
        await ApiAssert.StatusAsync(write, HttpStatusCode.NotFound, Diagnostics());
        await ApiAssert.StatusAsync(destroy, HttpStatusCode.NotFound, Diagnostics());

        // Indistinguishable from a project id that was never issued.
        var neverExisted = await intruder.GetAsync("/api/projects/987654");
        Assert.Equal(
            await read.Content.ReadAsStringAsync(),
            await neverExisted.Content.ReadAsStringAsync());

        // The intruder's own view is unaffected, and nothing leaked into it.
        var intruderProjects = await ApiAssert.StatusAndContentAsync<List<ProjectDto>>(
            await intruder.GetAsync("/api/projects"), HttpStatusCode.OK, Diagnostics());
        Assert.Empty(intruderProjects);
    }
}
