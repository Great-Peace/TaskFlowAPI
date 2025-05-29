using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskFlow.Core.DTOs
{
    public class CreateTaskDto
    {
        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        [Required]
        public int ProjectId { get; set; }

        public int? AssignedToId { get; set; }

        [MaxLength(50)]
        public string Priority { get; set; } = "Medium";

        public DateTime? DueDate { get; set; }
    }
}
