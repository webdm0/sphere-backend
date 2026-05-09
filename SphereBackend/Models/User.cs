using System.ComponentModel.DataAnnotations;

namespace SphereBackend.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required]
        [MinLength(3)]
        [MaxLength(20)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string NormalizedUsername { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string NormalizedEmail { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;
        public List<Board> Boards { get; set; } = new();
        public List<BoardMember> SharedBoards { get; set; } = new();
        public bool IsDemo { get; set; }
        public bool IsEmailConfirmed { get; set; } = false;
        public string? EmailConfirmationToken { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ConfirmationTokenExpiresAt { get; set; }
        public DateTime? LastConfirmationEmailSentAt { get; set; }
        public List<UserSession> Sessions { get; set; } = new();
    }
}
