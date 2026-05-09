using System.ComponentModel.DataAnnotations;

namespace SphereBackend.Models
{
    public class Board
    {
        public int Id { get; set; }
        [Required]
        [MinLength(1)]
        [MaxLength(100)]
        public string Title { get; set; } = string.Empty;
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public List<Column> Columns { get; set; } = new();
        public List<BoardMember> Members { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ArchivedAt { get; set; }
        public bool IsArchived => ArchivedAt.HasValue;
    }
}
