using System.ComponentModel.DataAnnotations;

namespace SphereBackend.Features.Boards
{
    public class CreateBoardDto
    {
        [Required(ErrorMessage = "Title is required.")]
        [MinLength(1, ErrorMessage = "Title is required.")]
        [MaxLength(100, ErrorMessage = "Title must be 100 characters or fewer.")]
        public string Title { get; set; } = string.Empty;

        public List<string> UserIds { get; set; } = new();
    }
}
