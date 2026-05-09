using SphereBackend.Features.Auth;
using SphereBackend.Services;
using System.Security.Claims;

namespace SphereBackend.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        public static int GetCurrentUserId(this ClaimsPrincipal user, IIdHasher idHasher)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (idHasher == null) throw new ArgumentNullException(nameof(idHasher));

            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userIdClaim))
                throw new InvalidOperationException("User ID claim is missing");

            if (idHasher.TryDecode(userIdClaim, out var userId))
                return userId;

            throw new InvalidOperationException("User ID claim is invalid");
        }

        public static bool IsDemoUser(this ClaimsPrincipal user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var value = user.FindFirst(DemoUserPolicy.IsDemoClaimType)?.Value;
            return bool.TryParse(value, out var isDemo) && isDemo;
        }
    }
}
