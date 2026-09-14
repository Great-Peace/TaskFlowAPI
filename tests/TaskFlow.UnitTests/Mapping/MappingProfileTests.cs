using AutoMapper;
using TaskFlow.Core.DTOs;
using TaskFlow.Core.Entities;
using TaskFlowAPI.Mapping;

namespace TaskFlow.UnitTests.Mapping;

/// <summary>
/// Behaviour of the object-to-DTO mapping.
/// </summary>
/// <remarks>
/// Mapping is configuration, and configuration fails silently: a renamed property
/// does not break the build, it just starts returning null to clients. These tests
/// pin the projections the API's responses actually depend on.
/// </remarks>
public class MappingProfileTests
{
    private readonly IMapper _mapper;

    public MappingProfileTests()
    {
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        _mapper = configuration.CreateMapper();
    }

    /// <summary>
    /// Every configured mapping can resolve every destination member.
    /// </summary>
    /// <remarks>
    /// One assertion that covers the whole profile. It is the test most likely to
    /// catch a mapping mistake introduced by renaming a property, and it costs
    /// nothing to maintain.
    /// </remarks>
    [Fact]
    public void The_mapping_configuration_is_valid()
    {
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());

        configuration.AssertConfigurationIsValid();
    }

    [Fact]
    public void Project_maps_its_owners_full_name_for_display()
    {
        var project = new Project
        {
            Id = 1,
            Name = "Apollo",
            Description = "Moon landing",
            Status = "Active",
            CreatedById = 7,
            CreatedBy = new User { Id = 7, FirstName = "Grace", LastName = "Hopper" }
        };

        var dto = _mapper.Map<ProjectDto>(project);

        Assert.Equal("Apollo", dto.Name);
        Assert.Equal(7, dto.CreatedById);
        Assert.Equal("Grace Hopper", dto.CreatedByName);
    }

    [Fact]
    public void Task_maps_its_project_name_and_assignee_name()
    {
        var task = new TaskItem
        {
            Id = 3,
            Title = "Write tests",
            Status = "Todo",
            Priority = "High",
            ProjectId = 1,
            Project = new Project { Id = 1, Name = "Apollo" },
            AssignedToId = 7,
            AssignedTo = new User { Id = 7, FirstName = "Grace", LastName = "Hopper" }
        };

        var dto = _mapper.Map<TaskItemDto>(task);

        Assert.Equal("Apollo", dto.ProjectName);
        Assert.Equal("Grace Hopper", dto.AssignedToName);
    }

    [Fact]
    public void An_unassigned_task_has_no_assignee_name()
    {
        // The mapping guards against a null navigation. Without the guard this throws,
        // and an unassigned task would fail to serialise at all.
        var task = new TaskItem
        {
            Id = 4,
            Title = "Unassigned work",
            ProjectId = 1,
            Project = new Project { Id = 1, Name = "Apollo" },
            AssignedToId = null,
            AssignedTo = null
        };

        var dto = _mapper.Map<TaskItemDto>(task);

        Assert.Null(dto.AssignedToName);
        Assert.Null(dto.AssignedToId);
    }

    [Fact]
    public void Creating_a_project_does_not_let_the_client_choose_server_owned_fields()
    {
        var request = new CreateProjectDto
        {
            Name = "Client supplied",
            Description = "Client supplied",
            Status = "Planning",
            StartDate = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var project = _mapper.Map<Project>(request);

        Assert.Equal("Client supplied", project.Name);
        Assert.Equal(new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc), project.StartDate);

        // Identity, ownership and audit fields are assigned by the server, never mapped
        // from the request.
        Assert.Equal(0, project.Id);
        Assert.Equal(0, project.CreatedById);
        Assert.Equal(default, project.CreatedAt);
    }

    /// <summary>
    /// Updating applies only the fields the client supplied.
    /// </summary>
    /// <remarks>
    /// <c>UpdateProjectDto</c> has nullable members and the profile maps them
    /// conditionally, giving patch semantics: omitting a field leaves the stored value
    /// alone rather than blanking it.
    /// </remarks>
    [Fact]
    public void Updating_a_project_leaves_omitted_fields_unchanged()
    {
        var existing = new Project
        {
            Id = 1,
            Name = "Original name",
            Description = "Original description",
            Status = "Planning",
            StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedById = 7
        };

        var update = new UpdateProjectDto { Status = "Active" };

        _mapper.Map(update, existing);

        Assert.Equal("Active", existing.Status);
        Assert.Equal("Original name", existing.Name);
        Assert.Equal("Original description", existing.Description);
        Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), existing.StartDate);
        Assert.Equal(7, existing.CreatedById);
    }

    [Fact]
    public void Updating_a_project_applies_every_field_the_client_did_supply()
    {
        var existing = new Project
        {
            Id = 1,
            Name = "Original name",
            Description = "Original description",
            Status = "Planning",
            CreatedById = 7
        };

        var update = new UpdateProjectDto
        {
            Name = "New name",
            Description = "New description",
            Status = "Completed",
            EndDate = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        };

        _mapper.Map(update, existing);

        Assert.Equal("New name", existing.Name);
        Assert.Equal("New description", existing.Description);
        Assert.Equal("Completed", existing.Status);
        Assert.Equal(new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc), existing.EndDate);
    }
}
