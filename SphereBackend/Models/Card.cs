using System.ComponentModel.DataAnnotations;

namespace SphereBackend.Models
{
    public class Card
    {
        public int Id { get; set; }
        [Required]
        [MinLength(1)]
        [MaxLength(80)]
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int? ColumnId { get; set; }
        public Column? Column { get; set; }
        public int BoardId { get; set; }
        public Board Board { get; set; } = null!;
        public int Order { get; set; }
        public string? Priority { get; set; }
        public int? AssigneeId { get; set; }
        public User? Assignee { get; set; }
        public DateOnly? StartAt { get; set; }
        public DateOnly? DueAt { get; set; }
        public int CreatedById { get; set; }
        public int? UpdatedById { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ArchivedAt { get; set; }
        public bool IsArchived => ArchivedAt.HasValue;
        public bool ArchivedManually { get; set; }
        public int? PreviousColumnId { get; set; }
    }
}
