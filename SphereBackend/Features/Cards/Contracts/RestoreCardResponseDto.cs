using SphereBackend.Features.Columns;

namespace SphereBackend.Features.Cards
{
    public class RestoreCardResponseDto
    {
        public CardDto? Card { get; set; }
        public string RestoreContext { get; set; } = null!;
        public ColumnShortDto? TargetColumn { get; set; }
    }
}
