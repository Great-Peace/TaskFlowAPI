using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskFlow.Core.DTOs
{
    public class UserProfileDto
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string FullName => $"{FirstName} {LastName}";

        // Recent activity
        public List<TaskItemDto> RecentTasks { get; set; } = new();
        public List<ProjectDto> RecentProjects { get; set; } = new();

        // Statistics
        public int TotalAssignedTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int OverdueTasks { get; set; }
        public int TotalCreatedProjects { get; set; }
        public int ActiveProjects { get; set; }
    }
}
