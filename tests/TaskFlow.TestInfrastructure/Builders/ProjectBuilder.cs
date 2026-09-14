using TaskFlow.Core.Entities;

namespace TaskFlow.TestInfrastructure.Builders;

/// <summary>
/// Builds <see cref="Project"/> entities for seeding.
/// </summary>
/// <remarks>
/// Timestamps default to fixed values rather than <c>DateTime.UtcNow</c> so that
/// ordering assertions are reproducible on any machine, at any time of day.
/// </remarks>
public sealed class ProjectBuilder
{
    private string _name = "Test Project";
    private string? _description = "Created by ProjectBuilder";
    private DateTime _startDate = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private DateTime? _endDate;
    private string _status = "Planning";
    private int _createdById;
    private DateTime _createdAt = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private DateTime? _updatedAt;

    public ProjectBuilder WithName(string name) { _name = name; return this; }

    public ProjectBuilder WithDescription(string? description) { _description = description; return this; }

    public ProjectBuilder WithStatus(string status) { _status = status; return this; }

    public ProjectBuilder WithStartDate(DateTime startDate) { _startDate = startDate; return this; }

    public ProjectBuilder WithEndDate(DateTime? endDate) { _endDate = endDate; return this; }

    /// <summary>Sets the owner. Required: projects are scoped to their creator.</summary>
    public ProjectBuilder OwnedBy(int userId) { _createdById = userId; return this; }

    /// <summary>Sets the owner from an already-persisted entity.</summary>
    public ProjectBuilder OwnedBy(User user) { _createdById = user.Id; return this; }

    public ProjectBuilder CreatedAt(DateTime createdAtUtc) { _createdAt = createdAtUtc; return this; }

    public ProjectBuilder UpdatedAt(DateTime? updatedAtUtc) { _updatedAt = updatedAtUtc; return this; }

    public Project Build() => new()
    {
        Name = _name,
        Description = _description,
        StartDate = _startDate,
        EndDate = _endDate,
        Status = _status,
        CreatedById = _createdById,
        CreatedAt = _createdAt,
        UpdatedAt = _updatedAt
    };
}
