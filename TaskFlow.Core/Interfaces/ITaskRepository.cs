using TaskFlow.Core.Entities;

namespace TaskFlow.Core.Interfaces
{
    public interface ITaskRepository : IGenericRepository<TaskItem>
    {
        Task<IEnumerable<TaskItem>> GetByProjectIdAsync(int projectId);

        Task<IEnumerable<TaskItem>> GetByAssigneeIdAsync(int userId);

        Task<IEnumerable<TaskItem>> GetOverdueTasksAsync();
    }
}
