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
/// HTTP behaviour of <c>/api/Projects</c>.
/// </summary>
public class ProjectsEndpointTests : IntegrationTestBase
{
    public ProjectsEndpointTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    // ------------------------------------------------------- authentication

    [Theory]
    [InlineData("GET", "/api/projects")]
    [InlineData("GET", "/api/projects/1")]
    [InlineData("POST", "/api/projects")]
    public async Task Protected_endpoints_reject_an_unauthenticated_caller(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new CreateProjectDto { Name = "Anything" });
        }

        var response = await Client.SendAsync(request);

        await ApiAssert.StatusAsync(response, HttpStatusCode.Unauthorized, Diagnostics());
    }

    [Fact]
    public async Task A_malformed_bearer_token_is_rejected()
    {
        using var client = Api.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var response = await client.GetAsync("/api/projects");

        await ApiAssert.StatusAsync(response, HttpStatusCode.Unauthorized, Diagnostics());
    }

    [Fact]
    public async Task A_token_signed_with_the_wrong_key_is_rejected()
    {
        // Structurally valid JWT, correct claims, wrong signature. Proves the API is
        // verifying the signature rather than merely parsing the token.
        const string forged =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
            "eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6IjEiLCJleHAiOjQ4NzY1NDMyMTAsImlzcyI6IlRhc2tGbG93QVBJLlRlc3RzIiwiYXVkIjoiVGFza0Zsb3dBUEkuVGVzdHMuQ2xpZW50cyJ9." +
            "ZmFrZS1zaWduYXR1cmUtdGhhdC1pcy1ub3QtdmFsaWQ";

        using var client = Api.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", forged);

        var response = await client.GetAsync("/api/projects");

        await ApiAssert.StatusAsync(response, HttpStatusCode.Unauthorized, Diagnostics());
    }

    // --------------------------------------------------------------- create

    [Fact]
    public async Task Creating_a_project_returns_201_with_a_location_header()
    {
        var client = await ClientForAsync(TestUsers.Alice);

        var response = await client.PostAsJsonAsync("/api/projects", new CreateProjectDto
        {
            Name = "Apollo",
            Description = "Moon landing",
            Status = "Planning",
            StartDate = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        var created = await ApiAssert.StatusAndContentAsync<ProjectDto>(
            response, HttpStatusCode.Created, Diagnostics());

        Assert.True(created.Id > 0);
        Assert.Equal("Apollo", created.Name);
        Assert.Equal("Moon landing", created.Description);
        Assert.Equal("Planning", created.Status);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task A_created_project_is_persisted_and_owned_by_its_creator()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        var client = await ClientForAsync(TestUsers.Alice);

        await client.PostAsJsonAsync("/api/projects", new CreateProjectDto { Name = "Apollo" });

        var project = await QueryAsync(db => db.Projects.SingleAsync());

        Assert.Equal("Apollo", project.Name);
        Assert.Equal(alice.Id, project.CreatedById);
    }

    /// <summary>
    /// A created project records when it was created.
    /// </summary>
    /// <remarks>
    /// The mapping ignores CreatedAt and the controller previously never set it, so
    /// every project persisted 0001-01-01. That also made
    /// <c>GetUserProjectsAsync</c>'s "newest first" ordering meaningless, because
    /// every row held the same value. The timestamp now comes from the injected clock.
    /// </remarks>
    [Fact]
    public async Task A_created_project_records_when_it_was_created()
    {
        var client = await ClientForAsync(TestUsers.Alice);

        await client.PostAsJsonAsync("/api/projects", new CreateProjectDto { Name = "Apollo" });

        var stored = await QueryAsync(db => db.Projects.SingleAsync());

        Assert.NotEqual(default, stored.CreatedAt);
        Assert.Equal(Api.Clock.GetUtcNow().UtcDateTime, stored.CreatedAt);
        Assert.Null(stored.UpdatedAt);
    }

    /// <summary>
    /// Creating and then fetching a project must describe it identically.
    /// </summary>
    /// <remarks>
    /// The create path previously mapped the entity before its owner navigation was
    /// loaded, so POST returned <c>createdByName: null</c> while GET returned the
    /// owner's name for the very same resource.
    /// </remarks>
    [Fact]
    public async Task Create_and_retrieve_return_the_same_representation()
    {
        var client = await ClientForAsync(TestUsers.Alice);

        var createResponse = await client.PostAsJsonAsync("/api/projects",
            new CreateProjectDto { Name = "Apollo", Description = "Moon landing" });
        var created = await ApiAssert.StatusAndContentAsync<ProjectDto>(
            createResponse, HttpStatusCode.Created, Diagnostics());

        var getResponse = await client.GetAsync($"/api/projects/{created.Id}");
        var fetched = await ApiAssert.StatusAndContentAsync<ProjectDto>(
            getResponse, HttpStatusCode.OK, Diagnostics());

        Assert.Equal(TestUsers.Alice.FullName, created.CreatedByName);
        Assert.Equal(fetched.CreatedByName, created.CreatedByName);
        Assert.Equal(fetched.Id, created.Id);
        Assert.Equal(fetched.Name, created.Name);
        Assert.Equal(fetched.Description, created.Description);
        Assert.Equal(fetched.Status, created.Status);
        Assert.Equal(fetched.CreatedById, created.CreatedById);
        Assert.Equal(fetched.CreatedAt, created.CreatedAt);
    }

    [Fact]
    public async Task The_location_header_of_a_created_project_resolves()
    {
        var client = await ClientForAsync(TestUsers.Alice);

        var create = await client.PostAsJsonAsync("/api/projects",
            new CreateProjectDto { Name = "Apollo" });
        var location = create.Headers.Location;

        Assert.NotNull(location);

        var followed = await client.GetAsync(location);

        var project = await ApiAssert.StatusAndContentAsync<ProjectDto>(
            followed, HttpStatusCode.OK, Diagnostics());
        Assert.Equal("Apollo", project.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Creating_a_project_without_a_name_is_rejected(string name)
    {
        var client = await ClientForAsync(TestUsers.Alice);

        var response = await client.PostAsJsonAsync("/api/projects", new CreateProjectDto { Name = name });

        await ApiAssert.ValidationErrorForAsync(response, "Name", Diagnostics());
        Assert.Equal(0, await QueryAsync(db => db.Projects.CountAsync()));
    }

    [Fact]
    public async Task Creating_a_project_with_an_over_long_name_is_rejected()
    {
        var client = await ClientForAsync(TestUsers.Alice);

        var response = await client.PostAsJsonAsync("/api/projects", new CreateProjectDto
        {
            // MaxLength(200) on the DTO; 201 characters must be refused before the
            // database rejects it with a truncation error.
            Name = new string('x', 201)
        });

        await ApiAssert.ValidationErrorForAsync(response, "Name", Diagnostics());
    }

    [Fact]
    public async Task A_name_at_the_maximum_length_is_accepted()
    {
        var client = await ClientForAsync(TestUsers.Alice);
        var name = new string('x', 200);

        var response = await client.PostAsJsonAsync("/api/projects", new CreateProjectDto { Name = name });

        await ApiAssert.StatusAsync(response, HttpStatusCode.Created, Diagnostics());
        Assert.Equal(name, await QueryAsync(db => db.Projects.Select(p => p.Name).SingleAsync()));
    }

    // ------------------------------------------------------------------ get

    [Fact]
    public async Task Listing_projects_returns_only_the_callers_own()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        var bob = await SeedUserAsync(TestUsers.Bob);

        await SeedAsync(
            new ProjectBuilder().WithName("Alice one").OwnedBy(alice).Build(),
            new ProjectBuilder().WithName("Alice two").OwnedBy(alice).Build(),
            new ProjectBuilder().WithName("Bob one").OwnedBy(bob).Build());

        var client = await ClientForAsync(TestUsers.Alice);
        var response = await client.GetAsync("/api/projects");

        var projects = await ApiAssert.StatusAndContentAsync<List<ProjectDto>>(
            response, HttpStatusCode.OK, Diagnostics());

        Assert.Equal(2, projects.Count);
        Assert.All(projects, p => Assert.Equal(alice.Id, p.CreatedById));
        Assert.DoesNotContain(projects, p => p.Name == "Bob one");
    }

    [Fact]
    public async Task Listing_projects_returns_an_empty_array_when_there_are_none()
    {
        var client = await ClientForAsync(TestUsers.Alice);

        var response = await client.GetAsync("/api/projects");

        var projects = await ApiAssert.StatusAndContentAsync<List<ProjectDto>>(
            response, HttpStatusCode.OK, Diagnostics());
        Assert.Empty(projects);
    }

    [Fact]
    public async Task Retrieving_a_project_returns_its_details_and_owner_name()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        var seeded = await SeedAsync(new ProjectBuilder()
            .WithName("Apollo")
            .WithDescription("Moon landing")
            .WithStatus("Active")
            .OwnedBy(alice)
            .Build());

        var client = await ClientForAsync(TestUsers.Alice);
        var response = await client.GetAsync($"/api/projects/{seeded[0].Id}");

        var project = await ApiAssert.StatusAndContentAsync<ProjectDto>(
            response, HttpStatusCode.OK, Diagnostics());

        Assert.Equal("Apollo", project.Name);
        Assert.Equal("Moon landing", project.Description);
        Assert.Equal("Active", project.Status);
        Assert.Equal(TestUsers.Alice.FullName, project.CreatedByName);
    }

    [Fact]
    public async Task Retrieving_a_project_includes_its_tasks()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        var projects = await SeedAsync(new ProjectBuilder().WithName("Apollo").OwnedBy(alice).Build());
        await SeedAsync(
            new TaskItemBuilder().WithTitle("Build rocket").InProject(projects[0]).AssignedTo(alice.Id).Build(),
            new TaskItemBuilder().WithTitle("Test rocket").InProject(projects[0]).Build());

        var client = await ClientForAsync(TestUsers.Alice);
        var response = await client.GetAsync($"/api/projects/{projects[0].Id}");

        var project = await ApiAssert.StatusAndContentAsync<ProjectDto>(
            response, HttpStatusCode.OK, Diagnostics());

        Assert.Equal(2, project.Tasks.Count);
        Assert.Contains(project.Tasks, t => t.Title == "Build rocket" && t.AssignedToName == TestUsers.Alice.FullName);
        Assert.Contains(project.Tasks, t => t.Title == "Test rocket" && t.AssignedToName == null);
    }

    [Fact]
    public async Task Retrieving_a_project_that_does_not_exist_returns_404()
    {
        var client = await ClientForAsync(TestUsers.Alice);

        var response = await client.GetAsync("/api/projects/999999");

        var error = await ApiAssert.ErrorAsync(response, HttpStatusCode.NotFound, Diagnostics());
        Assert.Equal("Project not found", error.Message);
    }

    /// <summary>
    /// A project must not be readable by anyone other than its owner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the broken-object-level-authorization defect found in the Phase 0
    /// assessment: <c>GET /api/Projects/{id}</c> loaded the project by id with no
    /// ownership check, so any authenticated user could read any project. The list
    /// endpoint was correctly scoped; only the by-id route leaked.
    /// </para>
    /// <para>
    /// The response is 404 rather than 403 deliberately. Returning 403 would confirm
    /// that a project with that id exists, letting a caller map out other users'
    /// data by probing ids. 404 makes "not yours" and "not there" indistinguishable.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_project_cannot_be_read_by_a_different_user()
    {
        var alice = await SeedUserAsync(TestUsers.Alice);
        await SeedUserAsync(TestUsers.Bob);

        var seeded = await SeedAsync(new ProjectBuilder()
            .WithName("Alice's confidential project")
            .OwnedBy(alice)
            .Build());

        var bobsClient = await ClientForAsync(TestUsers.Bob);
        var response = await bobsClient.GetAsync($"/api/projects/{seeded[0].Id}");

        await ApiAssert.StatusAsync(response, HttpStatusCode.NotFound, Diagnostics());

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("confidential", body);
    }
}
