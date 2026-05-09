namespace SphereBackend.Features.Cards
{
    public class CardDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? ColumnId { get; set; }
        public string BoardId { get; set; } = string.Empty;
        public int Order { get; set; }
        public string? Priority { get; set; }
        public string? AssigneeId { get; set; }
        public DateOnly? StartAt { get; set; }
        public DateOnly? DueAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public bool IsArchived { get; set; }
        public string? PreviousColumnId { get; set; }
    }
}
