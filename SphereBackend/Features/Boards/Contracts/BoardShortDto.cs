namespace SphereBackend.Features.Boards
{
    public class BoardShortDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool IsArchived { get; set; }
    }
}
