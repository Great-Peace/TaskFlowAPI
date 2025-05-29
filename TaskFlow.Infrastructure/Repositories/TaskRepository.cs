using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using TaskFlow.Core.Entities;
using TaskFlow.Core.Interfaces;
using TaskFlow.Infrastructure.Data;

namespace TaskFlow.Infrastructure.Repositories
{
    public class TaskRepository : GenericRepository<TaskItem>, ITaskRepository
    {
        public TaskRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<TaskItem>> GetByProjectIdAsync(int projectId)
        {
            return await _dbSet
                .Include(t => t.Project)
                .Include(t => t.AssignedTo)
                .Where(t => t.ProjectId == projectId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<TaskItem>> GetByAssigneeIdAsync(int userId)
        {
            return await _dbSet
                .Include(t => t.Project)
                .Include(t => t.AssignedTo)
                .Where(t => t.AssignedToId == userId)
                .OrderBy(t => t.DueDate)
                .ThenByDescending(t => t.Priority == "High")
                .ThenByDescending(t => t.Priority == "Medium")
                .ToListAsync();
        }

        public async Task<IEnumerable<TaskItem>> GetOverdueTasksAsync()
        {
            var currentDate = DateTime.UtcNow.Date;

            return await _dbSet
                .Include(t => t.Project)
                .Include(t => t.AssignedTo)
                .Where(t => t.DueDate.HasValue &&
                           t.DueDate.Value.Date < currentDate &&
                           t.Status != "Completed")
                .OrderBy(t => t.DueDate)
                .ToListAsync();
        }

        public override async Task<TaskItem?> GetByIdAsync(int id)
        {
            return await _dbSet
                .Include(t => t.Project)
                .Include(t => t.AssignedTo)
                .FirstOrDefaultAsync(t => t.Id == id);
        }

        public override async Task<IEnumerable<TaskItem>> GetAllAsync()
        {
            return await _dbSet
                .Include(t => t.Project)
                .Include(t => t.AssignedTo)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public override async Task<IEnumerable<TaskItem>> FindAsync(Expression<Func<TaskItem, bool>> predicate)
        {
            return await _dbSet
                .Include(t => t.Project)
                .Include(t => t.AssignedTo)
                .Where(predicate)
                .ToListAsync();
        }

        // Helper methods for task management
        public async Task<IEnumerable<TaskItem>> GetTasksByStatusAsync(string status)
        {
            return await _dbSet
                .Include(t => t.Project)
                .Include(t => t.AssignedTo)
                .Where(t => t.Status == status)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<TaskItem>> GetTasksByPriorityAsync(string priority)
        {
            return await _dbSet
                .Include(t => t.Project)
                .Include(t => t.AssignedTo)
                .Where(t => t.Priority == priority)
                .OrderBy(t => t.DueDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<TaskItem>> GetUpcomingTasksAsync(int userId, int days = 7)
        {
            var endDate = DateTime.UtcNow.AddDays(days).Date;
            var currentDate = DateTime.UtcNow.Date;

            return await _dbSet
                .Include(t => t.Project)
                .Include(t => t.AssignedTo)
                .Where(t => t.AssignedToId == userId &&
                           t.DueDate.HasValue &&
                           t.DueDate.Value.Date >= currentDate &&
                           t.DueDate.Value.Date <= endDate &&
                           t.Status != "Completed")
                .OrderBy(t => t.DueDate)
                .ToListAsync();
        }
    }
}