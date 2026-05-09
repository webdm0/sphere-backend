namespace SphereBackend.Features.Columns
{
    public class ReorderColumnsRequestDto
    {
        public string BoardId { get; set; } = string.Empty;
        public List<ColumnOrderDto> Columns { get; set; } = new();
    }
}
