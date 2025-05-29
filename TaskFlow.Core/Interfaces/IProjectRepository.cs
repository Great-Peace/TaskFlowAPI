using TaskFlow.Core.Entities;

namespace TaskFlow.Core.Interfaces
{
    public interface IProjectRepository : IGenericRepository<Project>
    {
        Task<IEnumerable<Project>> GetUserProjectsAsync(int userId);

        Task<Project?> GetWithTasksAsync(int id);
    }
}
