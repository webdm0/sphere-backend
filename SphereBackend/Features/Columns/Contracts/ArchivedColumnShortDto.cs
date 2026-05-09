using SphereBackend.Features.Cards;

namespace SphereBackend.Features.Columns
{
    public class ArchivedColumnShortDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Order { get; set; }
        public List<ArchivedCardDto> Cards { get; set; } = new();
        public DateTime? ArchivedAt { get; set; }
    }
}
