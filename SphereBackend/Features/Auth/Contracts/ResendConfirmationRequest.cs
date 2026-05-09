using System.ComponentModel.DataAnnotations;

namespace SphereBackend.Features.Auth
{
    public class ResendConfirmationRequest
    {
        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [MaxLength(255, ErrorMessage = "Email address must be 255 characters or fewer.")]
        [RegularExpression(@"^[\x21-\x7E]+$", ErrorMessage = "Use an email address with English letters, numbers, and standard symbols only.")]
        public string Email { get; set; } = string.Empty;
    }
}
