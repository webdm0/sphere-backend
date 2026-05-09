using SphereBackend.Data;
using SphereBackend.Infrastructure.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SphereBackend.Features.Auth
{
    public interface ILoginHandler
    {
        Task<IActionResult> HandleAsync(
            LoginRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken = default);
    }

    public sealed class LoginHandler : ILoginHandler
    {
        private readonly AppDbContext _context;
        private readonly IAuthFlowService _authFlow;

        public LoginHandler(AppDbContext context, IAuthFlowService authFlow)
        {
            _context = context;
            _authFlow = authFlow;
        }

        public async Task<IActionResult> HandleAsync(
            LoginRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken = default)
        {
            var ipAddress = _authFlow.GetClientIp(httpContext);
            var cacheKey = $"login_limit_{ipAddress}";
            var normalizedIdentifierLookup = _authFlow.NormalizeForLookup(request.Identifier);

            var banInfo = _authFlow.GetBanInfo(cacheKey);
            if (banInfo.BannedUntil.HasValue && banInfo.BannedUntil > DateTime.UtcNow)
            {
                return TooManyAttemptsResponse(banInfo, httpContext);
            }

            var user = await _context.Users
                .Include(u => u.Sessions)
                .FirstOrDefaultAsync(u =>
                    u.NormalizedEmail == normalizedIdentifierLookup ||
                    u.NormalizedUsername == normalizedIdentifierLookup,
                    cancellationToken);

            if (user == null || !user.IsEmailConfirmed || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                _authFlow.RegisterAttempt(cacheKey, banInfo);

                if (banInfo.BannedUntil.HasValue && banInfo.BannedUntil > DateTime.UtcNow)
                {
                    return TooManyAttemptsResponse(banInfo, httpContext);
                }

                return new UnauthorizedObjectResult("Incorrect email, username, or password.");
            }

            _authFlow.RemoveBanInfo(cacheKey);

            var accessToken = await _authFlow.CreateSessionAndGetTokenAsync(user, httpContext, cancellationToken);

            return new OkObjectResult(new { accessToken });
        }

        private static ObjectResult TooManyAttemptsResponse(UserBanInfo banInfo, HttpContext httpContext)
        {
            var retryAfterUtc = banInfo.BannedUntil!.Value;
            var remaining = retryAfterUtc - DateTime.UtcNow;
            var problem = ApiProblemDetailsFactory.CreateTooManyRequests(
                $"Too many attempts. Please wait {Math.Ceiling(remaining.TotalMinutes)} minutes.",
                retryAfterUtc,
                httpContext);

            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status429TooManyRequests
            };
        }
    }
}
