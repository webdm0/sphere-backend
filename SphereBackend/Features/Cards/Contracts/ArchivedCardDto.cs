namespace SphereBackend.Features.Cards
{
    public class ArchivedCardDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Order { get; set; }
        public string? ColumnId { get; set; }
        public string? PreviousColumnId { get; set; }
        public string? AssigneeId { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public bool ArchivedManually { get; set; }
        public string? ColumnTitle { get; set; }
        public string ColumnStatus { get; set; } = "NoColumn";
    }
}
