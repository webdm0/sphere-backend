using System.ComponentModel.DataAnnotations;

namespace SphereBackend.Models
{
    public class Column
    {
        public int Id { get; set; }
        [Required]
        [MinLength(1)]
        [MaxLength(32)]
        public string Title { get; set; } = string.Empty;
        public int BoardId { get; set; }
        public Board Board { get; set; } = null!;
        public List<Card> Cards { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int Order { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public bool IsArchived => ArchivedAt.HasValue;
    }
}
