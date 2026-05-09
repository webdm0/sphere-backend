namespace SphereBackend.Models
{
    public class BoardMember
    {
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public int BoardId { get; set; }
        public Board Board { get; set; } = null!;
        public int Order { get; set; }
        public bool IsAccepted { get; set; } = false;
        public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    }
}
