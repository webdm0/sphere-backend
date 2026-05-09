namespace SphereBackend.Features.Cards
{
    public class ReorderCardsRequestDto
    {
        public string TargetColumnId { get; set; } = string.Empty;
        public List<CardOrderDto> Cards { get; set; } = new();
    }
}
