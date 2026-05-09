namespace SphereBackend.Features.Auth
{
    public class UserBanInfo
    {
        public int Attempts { get; set; }
        public DateTime? BannedUntil { get; set; }
    }
}
