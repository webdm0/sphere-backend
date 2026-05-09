using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SphereBackend.Features.Cards
{
    public class CreateCardDto
    {
        [Required(ErrorMessage = "Title is required.")]
        [MinLength(1, ErrorMessage = "Title is required.")]
        [MaxLength(80, ErrorMessage = "Title must be 80 characters or fewer.")]
        public string Title { get; set; } = string.Empty;

        [MaxLength(CardValidationRules.MaxContentLength, ErrorMessage = CardValidationRules.ContentTooLongMessage)]
        public string? Content { get; set; }
        public string ColumnId { get; set; } = string.Empty;

        [JsonPropertyName("assignToMe")]
        public bool AssignToMe { get; set; }
    }
}
