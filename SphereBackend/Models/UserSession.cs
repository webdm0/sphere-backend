namespace SphereBackend.Models
{
    public class UserSession
    {
        public int Id { get; set; }
        public string RefreshTokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
        public string? UserAgent { get; set; }
        public string? IpAddress { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
    }

}
