using System.ComponentModel.DataAnnotations;

namespace SphereBackend.Features.Boards
{
    public class AddMemberDto
    {
        [Required(ErrorMessage = "Select a user.")]
        public string UserId { get; set; } = string.Empty;
    }
}
