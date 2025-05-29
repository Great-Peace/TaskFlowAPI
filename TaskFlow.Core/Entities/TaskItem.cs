using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskFlow.Core.Entities
{
    public class TaskItem
    {
        public int Id { get; set; }

        [Required, MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        [Required, MaxLength(50)]
        public string Status { get; set; } = "Todo";

        [Required, MaxLength(50)]
        public string Priority { get; set; } = "Medium";

        public DateTime? DueDate { get; set; }

        public int ProjectId { get; set; }

        public int? AssignedToId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public virtual Project Project { get; set; } = null!;

        public virtual User? AssignedTo { get; set; }
    }
}
