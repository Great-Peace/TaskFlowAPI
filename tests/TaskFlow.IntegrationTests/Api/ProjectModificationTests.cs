using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Core.DTOs;
using TaskFlow.TestInfrastructure.Builders;
using TaskFlow.TestInfrastructure.Fixtures;
using TaskFlow.TestInfrastructure.Http;
using TaskFlow.TestInfrastructure.TestData;

namespace TaskFlow.IntegrationTests.Api;

/// <summary>
/// HTTP behaviour of updating and deleting projects, and of the administrator
/// oversight endpoint.
/// </summary>
public class ProjectModificationTests : IntegrationTestBase
{
    public ProjectModificationTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    // --------------------------------------------------------------- update

    [Fact]
    public async Task Updating_a_project_returns_the_updated_representation()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        var seeded = await SeedAsync(new ProjectBuilder()
            .WithName("Apollo").WithStatus("Planning").OwnedBy(alice).Build());

        var client = await ClientForAsync(TestUsers.Alice);
        var response = await client.PutAsJsonAsync($"/api/projects/{seeded[0].Id}", new UpdateProjectDto
        {
            Name = "Apollo 11",
            Status = "Active"
        });

        var updated = await ApiAssert.StatusAndContentAsync<ProjectDto>(
            response, HttpStatusCode.OK, Diagnostics());

        Assert.Equal("Apollo 11", updated.Name);
        Assert.Equal("Active", updated.Status);
        Assert.Equal(TestUsers.Alice.FullName, updated.CreatedByName);
    }

    [Fact]
    public async Task An_update_is_persisted()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        var seeded = await SeedAsync(new ProjectBuilder()
            .WithName("Apollo").WithStatus("Planning").OwnedBy(alice).Build());

        var client = await ClientForAsync(TestUsers.Alice);
        await client.PutAsJsonAsync($"/api/projects/{seeded[0].Id}",
            new UpdateProjectDto { Status = "Active" });

        var stored = await QueryAsync(db => db.Projects.SingleAsync());
        Assert.Equal("Active", stored.Status);
    }

    /// <summary>
    /// Omitted fields must keep their stored values.
    /// </summary>
    /// <remarks>
    /// This is the end-to-end counterpart of the mapping defect found in Phase 2,
    /// where a blanket null check ran against the converted value and overwrote
    /// StartDate with 0001-01-01 whenever it was omitted.
    /// </remarks>
    [Fact]
    public async Task Updating_only_one_field_leaves_the_others_untouched()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        var start = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var seeded = await SeedAsync(new ProjectBuilder()
            .WithName("Apollo")
            .WithDescription("Moon landing")
            .WithStatus("Planning")
            .WithStartDate(start)
            .OwnedBy(alice)
            .Build());

        var client = await ClientForAsync(TestUsers.Alice);
        await client.PutAsJsonAsync($"/api/projects/{seeded[0].Id}",
            new UpdateProjectDto { Status = "Active" });

        var stored = await QueryAsync(db => db.Projects.SingleAsync());

        Assert.Equal("Active", stored.Status);
        Assert.Equal("Apollo", stored.Name);
        Assert.Equal("Moon landing", stored.Description);
        Assert.Equal(start, stored.StartDate);
    }

    [Fact]
    public async Task An_update_stamps_the_modification_time()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        var seeded = await SeedAsync(new ProjectBuilder().OwnedBy(alice).Build());

        var client = await ClientForAsync(TestUsers.Alice);
        await client.PutAsJsonAsync($"/api/projects/{seeded[0].Id}",
            new UpdateProjectDto { Status = "Active" });

        var stored = await QueryAsync(db => db.Projects.SingleAsync());

        // The clock is frozen, so this is an exact comparison rather than a window.
        Assert.Equal(Api.Clock.GetUtcNow().UtcDateTime, stored.UpdatedAt);
    }

    [Fact]
    public async Task Updating_a_project_that_does_not_exist_returns_404()
    {
        var client = await ClientForAsync(TestUsers.Alice);

        var response = await client.PutAsJsonAsync("/api/projects/999999",
            new UpdateProjectDto { Status = "Active" });

        await ApiAssert.StatusAsync(response, HttpStatusCode.NotFound, Diagnostics());
    }

    [Fact]
    public async Task A_project_cannot_be_updated_by_a_different_user()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        await SeedUserAsync(TestUsers.Bob);
        var seeded = await SeedAsync(new ProjectBuilder()
            .WithName("Alice's project").WithStatus("Planning").OwnedBy(alice).Build());

        var bobsClient = await ClientForAsync(TestUsers.Bob);
        var response = await bobsClient.PutAsJsonAsync($"/api/projects/{seeded[0].Id}",
            new UpdateProjectDto { Name = "Bob was here" });

        await ApiAssert.StatusAsync(response, HttpStatusCode.NotFound, Diagnostics());

        // The rejection must also mean nothing changed.
        var stored = await QueryAsync(db => db.Projects.SingleAsync());
        Assert.Equal("Alice's project", stored.Name);
    }

    [Fact]
    public async Task Updating_requires_authentication()
    {
        var response = await Client.PutAsJsonAsync("/api/projects/1",
            new UpdateProjectDto { Status = "Active" });

        await ApiAssert.StatusAsync(response, HttpStatusCode.Unauthorized, Diagnostics());
    }

    [Fact]
    public async Task An_over_long_name_is_rejected_on_update()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        var seeded = await SeedAsync(new ProjectBuilder().OwnedBy(alice).Build());

        var client = await ClientForAsync(TestUsers.Alice);
        var response = await client.PutAsJsonAsync($"/api/projects/{seeded[0].Id}",
            new UpdateProjectDto { Name = new string('x', 201) });

        await ApiAssert.ValidationErrorForAsync(response, "Name", Diagnostics());
    }

    // --------------------------------------------------------------- delete

    [Fact]
    public async Task Deleting_a_project_returns_204_and_removes_it()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        var seeded = await SeedAsync(new ProjectBuilder().OwnedBy(alice).Build());

        var client = await ClientForAsync(TestUsers.Alice);
        var response = await client.DeleteAsync($"/api/projects/{seeded[0].Id}");

        await ApiAssert.StatusAsync(response, HttpStatusCode.NoContent, Diagnostics());
        Assert.Equal(0, await QueryAsync(db => db.Projects.CountAsync()));
    }

    [Fact]
    public async Task Deleting_a_project_also_removes_its_tasks()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        var projects = await SeedAsync(new ProjectBuilder().OwnedBy(alice).Build());
        await SeedAsync(
            new TaskItemBuilder().WithTitle("Task one").InProject(projects[0]).Build(),
            new TaskItemBuilder().WithTitle("Task two").InProject(projects[0]).Build());

        var client = await ClientForAsync(TestUsers.Alice);
        await client.DeleteAsync($"/api/projects/{projects[0].Id}");

        // The model configures cascade delete from Project to TaskItem. This asserts
        // that the configured behaviour is what SQL Server actually does.
        Assert.Equal(0, await QueryAsync(db => db.Tasks.CountAsync()));
    }

    [Fact]
    public async Task A_deleted_project_can_no_longer_be_retrieved()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        var seeded = await SeedAsync(new ProjectBuilder().OwnedBy(alice).Build());

        var client = await ClientForAsync(TestUsers.Alice);
        await client.DeleteAsync($"/api/projects/{seeded[0].Id}");

        var response = await client.GetAsync($"/api/projects/{seeded[0].Id}");
        await ApiAssert.StatusAsync(response, HttpStatusCode.NotFound, Diagnostics());
    }

    [Fact]
    public async Task Deleting_the_same_project_twice_returns_404_the_second_time()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        var seeded = await SeedAsync(new ProjectBuilder().OwnedBy(alice).Build());

        var client = await ClientForAsync(TestUsers.Alice);

        var first = await client.DeleteAsync($"/api/projects/{seeded[0].Id}");
        var second = await client.DeleteAsync($"/api/projects/{seeded[0].Id}");

        await ApiAssert.StatusAsync(first, HttpStatusCode.NoContent, Diagnostics());
        await ApiAssert.StatusAsync(second, HttpStatusCode.NotFound, Diagnostics());
    }

    [Fact]
    public async Task A_project_cannot_be_deleted_by_a_different_user()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        await SeedUserAsync(TestUsers.Bob);
        var seeded = await SeedAsync(new ProjectBuilder().OwnedBy(alice).Build());

        var bobsClient = await ClientForAsync(TestUsers.Bob);
        var response = await bobsClient.DeleteAsync($"/api/projects/{seeded[0].Id}");

        await ApiAssert.StatusAsync(response, HttpStatusCode.NotFound, Diagnostics());
        Assert.Equal(1, await QueryAsync(db => db.Projects.CountAsync()));
    }

    [Fact]
    public async Task Deleting_requires_authentication()
    {
        var response = await Client.DeleteAsync("/api/projects/1");

        await ApiAssert.StatusAsync(response, HttpStatusCode.Unauthorized, Diagnostics());
    }

    // ------------------------------------------------- role-based authorization

    [Fact]
    public async Task An_administrator_can_list_every_users_projects()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        var bob = await SeedUserAsync(TestUsers.Bob);
        await SeedAsync(
            new ProjectBuilder().WithName("Alice's project").OwnedBy(alice).Build(),
            new ProjectBuilder().WithName("Bob's project").OwnedBy(bob).Build());

        var adminClient = await ClientForAsync(TestUsers.Admin);
        var response = await adminClient.GetAsync("/api/projects/all");

        var projects = await ApiAssert.StatusAndContentAsync<List<ProjectDto>>(
            response, HttpStatusCode.OK, Diagnostics());

        Assert.Equal(2, projects.Count);
        Assert.Contains(projects, p => p.Name == "Alice's project");
        Assert.Contains(projects, p => p.Name == "Bob's project");
    }

    /// <summary>
    /// An authenticated non-administrator must be refused.
    /// </summary>
    /// <remarks>
    /// 403 rather than 404 here, unlike the ownership checks. The endpoint's existence
    /// is not a secret - it is in the published contract - so hiding it would reveal
    /// nothing while making a permissions problem look like a routing bug.
    /// </remarks>
    [Fact]
    public async Task An_ordinary_user_is_forbidden_from_the_administrator_endpoint()
    {
        var client = await ClientForAsync(TestUsers.Alice);

        var response = await client.GetAsync("/api/projects/all");

        await ApiAssert.StatusAsync(response, HttpStatusCode.Forbidden, Diagnostics());
    }

    [Fact]
    public async Task The_administrator_endpoint_rejects_an_unauthenticated_caller()
    {
        var response = await Client.GetAsync("/api/projects/all");

        await ApiAssert.StatusAsync(response, HttpStatusCode.Unauthorized, Diagnostics());
    }

    /// <summary>
    /// The literal "all" route must not be captured by the {id} route.
    /// </summary>
    [Fact]
    public async Task The_administrator_route_is_not_shadowed_by_the_by_id_route()
    {
        var client = await ClientForAsync(TestUsers.Alice);

        // An ordinary user hitting /all gets 403 from the role check, not the 404 that
        // the by-id route would produce for a project called "all".
        var response = await client.GetAsync("/api/projects/all");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
