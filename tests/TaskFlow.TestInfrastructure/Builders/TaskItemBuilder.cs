using TaskFlow.Core.Entities;

namespace TaskFlow.TestInfrastructure.Builders;

/// <summary>
/// Builds <see cref="TaskItem"/> entities for seeding.
/// </summary>
/// <remarks>
/// The API exposes no task endpoints today, so this builder is used to seed the
/// child collection that <c>GET /api/Projects/{id}</c> returns, and to exercise
/// the repository layer directly.
/// </remarks>
public sealed class TaskItemBuilder
{
    private string _title = "Test Task";
    private string? _description = "Created by TaskItemBuilder";
    private string _status = "Todo";
    private string _priority = "Medium";
    private DateTime? _dueDate;
    private int _projectId;
    private int? _assignedToId;
    private DateTime _createdAt = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public TaskItemBuilder WithTitle(string title) { _title = title; return this; }

    public TaskItemBuilder WithDescription(string? description) { _description = description; return this; }

    public TaskItemBuilder WithStatus(string status) { _status = status; return this; }

    public TaskItemBuilder WithPriority(string priority) { _priority = priority; return this; }

    public TaskItemBuilder DueOn(DateTime? dueDate) { _dueDate = dueDate; return this; }

    public TaskItemBuilder InProject(int projectId) { _projectId = projectId; return this; }

    public TaskItemBuilder InProject(Project project) { _projectId = project.Id; return this; }

    public TaskItemBuilder AssignedTo(int? userId) { _assignedToId = userId; return this; }

    public TaskItemBuilder CreatedAt(DateTime createdAtUtc) { _createdAt = createdAtUtc; return this; }

    public TaskItem Build() => new()
    {
        Title = _title,
        Description = _description,
        Status = _status,
        Priority = _priority,
        DueDate = _dueDate,
        ProjectId = _projectId,
        AssignedToId = _assignedToId,
        CreatedAt = _createdAt
    };
}
