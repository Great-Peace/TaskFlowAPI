using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using TaskFlow.Core.Entities;
using TaskFlow.Core.Interfaces;
using TaskFlow.Infrastructure.Data;

namespace TaskFlow.Infrastructure.Repositories
{
    public class ProjectRepository : GenericRepository<Project>, IProjectRepository
    {
        public ProjectRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<Project>> GetUserProjectsAsync(int userId)
        {
            return await _dbSet
                .Include(p => p.CreatedBy)
                .Where(p => p.CreatedById == userId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        public async Task<Project?> GetWithTasksAsync(int id)
        {
            return await _dbSet
                .Include(p => p.CreatedBy)
                .Include(p => p.Tasks)
                    .ThenInclude(t => t.AssignedTo)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public override async Task<Project?> GetByIdAsync(int id)
        {
            return await _dbSet
                .Include(p => p.CreatedBy)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public override async Task<IEnumerable<Project>> GetAllAsync()
        {
            return await _dbSet
                .Include(p => p.CreatedBy)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }
    }
}