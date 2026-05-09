using SphereBackend.Models;

namespace SphereBackend.Features.Auth
{
    public static class DemoUserPolicy
    {
        public const string IsDemoClaimType = "is_demo";
        public const string ExpiresAtClaimType = "demo_exp";
        public const string RestrictedFeatureMessage = "Demo accounts can't use this feature.";
        public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

        public static DateTime GetExpiresAtUtc(User user)
        {
            ArgumentNullException.ThrowIfNull(user);
            return user.CreatedAt.Add(Lifetime);
        }

        public static bool IsExpired(User user, DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(user);
            return user.IsDemo && GetExpiresAtUtc(user) <= utcNow;
        }
    }
}
