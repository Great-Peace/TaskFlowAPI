using AutoMapper;
using TaskFlow.Core.DTOs;
using TaskFlow.Core.Entities;

namespace TaskFlowAPI.Mapping
{
    /// <summary>
    /// Entity/DTO projections used by the API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Patch semantics.</b> The Update* DTOs have nullable members: a member left
    /// null means "leave this value alone". That is expressed with an explicit
    /// <c>Condition</c> on each member, checking the <i>source</i> property.
    /// </para>
    /// <para>
    /// A blanket <c>ForAllMembers(opt =&gt; opt.Condition((src, dest, srcMember) =&gt;
    /// srcMember != null))</c> looks equivalent but is not, and was silently corrupting
    /// data. For a nullable source mapped onto a non-nullable destination - such as
    /// <c>DateTime?</c> onto <c>DateTime</c> - the value handed to the condition has
    /// already been converted, so a null source arrives as <c>default(DateTime)</c>.
    /// That is not null, the condition passes, and the stored date is overwritten with
    /// <c>0001-01-01</c>. Checking the source property directly avoids the conversion
    /// entirely.
    /// </para>
    /// <para>
    /// <b>Server-owned fields.</b> Identity, ownership and audit columns are ignored on
    /// every inbound mapping so that a request body can never set them. They are
    /// assigned by the controller, with timestamps taken from the injected
    /// <see cref="TimeProvider"/> rather than from the mapping.
    /// </para>
    /// </remarks>
    public class MappingProfile : Profile
    {
        /// <summary>Configures the mappings.</summary>
        public MappingProfile()
        {
            // ------------------------------------------------------------- users

            CreateMap<User, UserDto>()
                .ForMember(dest => dest.AssignedTasksCount,
                          opt => opt.MapFrom(src => src.AssignedTasks.Count))
                .ForMember(dest => dest.CreatedProjectsCount,
                          opt => opt.MapFrom(src => src.CreatedProjects.Count));

            CreateMap<CreateUserDto, User>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.PasswordHash, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.AssignedTasks, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedProjects, opt => opt.Ignore());

            CreateMap<UpdateUserDto, User>()
                .ForMember(dest => dest.FirstName, opt => opt.Condition(src => src.FirstName != null))
                .ForMember(dest => dest.LastName, opt => opt.Condition(src => src.LastName != null))
                .ForMember(dest => dest.Email, opt => opt.Condition(src => src.Email != null))
                .ForMember(dest => dest.Role, opt => opt.Condition(src => src.Role != null))
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.PasswordHash, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.AssignedTasks, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedProjects, opt => opt.Ignore());

            CreateMap<RegisterDto, User>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.PasswordHash, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.AssignedTasks, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedProjects, opt => opt.Ignore())
                .ForMember(dest => dest.Role, opt => opt.MapFrom(src => "User"));

            CreateMap<User, UserProfileDto>()
                .ForMember(dest => dest.TotalAssignedTasks,
                          opt => opt.MapFrom(src => src.AssignedTasks.Count))
                .ForMember(dest => dest.CompletedTasks,
                          opt => opt.MapFrom(src => src.AssignedTasks.Count(t => t.Status == "Completed")))
                .ForMember(dest => dest.OverdueTasks,
                          opt => opt.MapFrom(src => src.AssignedTasks.Count(t =>
                              t.DueDate.HasValue && t.DueDate.Value.Date < DateTime.UtcNow.Date && t.Status != "Completed")))
                .ForMember(dest => dest.TotalCreatedProjects,
                          opt => opt.MapFrom(src => src.CreatedProjects.Count))
                .ForMember(dest => dest.ActiveProjects,
                          opt => opt.MapFrom(src => src.CreatedProjects.Count(p => p.Status == "Active")))
                .ForMember(dest => dest.RecentTasks,
                          opt => opt.MapFrom(src => src.AssignedTasks.OrderByDescending(t => t.CreatedAt).Take(5)))
                .ForMember(dest => dest.RecentProjects,
                          opt => opt.MapFrom(src => src.CreatedProjects.OrderByDescending(p => p.CreatedAt).Take(3)));

            // ---------------------------------------------------------- projects

            CreateMap<Project, ProjectDto>()
                .ForMember(dest => dest.CreatedByName,
                          opt => opt.MapFrom(src => $"{src.CreatedBy.FirstName} {src.CreatedBy.LastName}"));

            CreateMap<CreateProjectDto, Project>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedById, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.Tasks, opt => opt.Ignore());

            CreateMap<UpdateProjectDto, Project>()
                .ForMember(dest => dest.Name, opt => opt.Condition(src => src.Name != null))
                .ForMember(dest => dest.Description, opt => opt.Condition(src => src.Description != null))
                .ForMember(dest => dest.Status, opt => opt.Condition(src => src.Status != null))
                .ForMember(dest => dest.StartDate, opt => opt.Condition(src => src.StartDate.HasValue))
                .ForMember(dest => dest.EndDate, opt => opt.Condition(src => src.EndDate.HasValue))
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedById, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.Tasks, opt => opt.Ignore());

            // ------------------------------------------------------------- tasks

            CreateMap<TaskItem, TaskItemDto>()
                .ForMember(dest => dest.ProjectName, opt => opt.MapFrom(src => src.Project.Name))
                .ForMember(dest => dest.AssignedToName,
                          opt => opt.MapFrom(src => src.AssignedTo != null ?
                                           $"{src.AssignedTo.FirstName} {src.AssignedTo.LastName}" : null));

            CreateMap<CreateTaskDto, TaskItem>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => "Todo"))
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.Project, opt => opt.Ignore())
                .ForMember(dest => dest.AssignedTo, opt => opt.Ignore());

            CreateMap<UpdateTaskDto, TaskItem>()
                .ForMember(dest => dest.Title, opt => opt.Condition(src => src.Title != null))
                .ForMember(dest => dest.Description, opt => opt.Condition(src => src.Description != null))
                .ForMember(dest => dest.Status, opt => opt.Condition(src => src.Status != null))
                .ForMember(dest => dest.Priority, opt => opt.Condition(src => src.Priority != null))
                .ForMember(dest => dest.DueDate, opt => opt.Condition(src => src.DueDate.HasValue))
                .ForMember(dest => dest.AssignedToId, opt => opt.Condition(src => src.AssignedToId.HasValue))
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.ProjectId, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())

                // Set by the controller from the injected clock, not by the mapping,
                // so that the update timestamp is deterministic and testable.
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.Project, opt => opt.Ignore())
                .ForMember(dest => dest.AssignedTo, opt => opt.Ignore());
        }
    }
}
