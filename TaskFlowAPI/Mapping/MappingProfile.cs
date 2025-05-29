using AutoMapper;
using TaskFlow.Core.DTOs;
using TaskFlow.Core.Entities;

namespace TaskFlowAPI.Mapping
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // User mappings
            CreateMap<User, UserDto>()
                .ForMember(dest => dest.AssignedTasksCount,
                          opt => opt.MapFrom(src => src.AssignedTasks.Count))
                .ForMember(dest => dest.CreatedProjectsCount,
                          opt => opt.MapFrom(src => src.CreatedProjects.Count));

            CreateMap<CreateUserDto, User>()
                .ForMember(dest => dest.PasswordHash, opt => opt.Ignore())
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

            CreateMap<UpdateUserDto, User>()
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));

            CreateMap<RegisterDto, User>()
                .ForMember(dest => dest.PasswordHash, opt => opt.Ignore())
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.Role, opt => opt.MapFrom(src => "User"));

            // User profile mapping with statistics
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

            // Project mappings
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
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));

            // Task mappings
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
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.UtcNow))
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));
        }
    }
}
