using System.ComponentModel.DataAnnotations;

namespace SphereBackend.Features.Columns
{
    public class CreateColumnDto
    {
        [Required(ErrorMessage = "Title is required.")]
        [MinLength(1, ErrorMessage = "Title is required.")]
        [MaxLength(32, ErrorMessage = "Title must be 32 characters or fewer.")]
        public string Title { get; set; } = string.Empty;
        public string BoardId { get; set; } = string.Empty;
    }
}
