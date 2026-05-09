using SphereBackend.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SphereBackend.Features.Auth
{
    public interface IRefreshSessionHandler
    {
        Task<IActionResult> HandleAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken = default);
    }

    public sealed class RefreshSessionHandler : IRefreshSessionHandler
    {
        private readonly AppDbContext _context;
        private readonly IAuthFlowService _authFlow;

        public RefreshSessionHandler(AppDbContext context, IAuthFlowService authFlow)
        {
            _context = context;
            _authFlow = authFlow;
        }

        public async Task<IActionResult> HandleAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken = default)
        {
            var refreshToken = _authFlow.ReadRefreshTokenFromCookie(httpContext);
            if (string.IsNullOrEmpty(refreshToken))
            {
                _authFlow.ClearAuthCookies(httpContext);
                return new UnauthorizedObjectResult("Session expired.");
            }

            var refreshHash = _authFlow.HashRefresh(refreshToken);

            var session = await _context.UserSessions
                .Include(s => s.User)
                .FirstOrDefaultAsync(s =>
                    s.RefreshTokenHash == refreshHash &&
                    s.ExpiresAt > DateTime.UtcNow,
                    cancellationToken);

            if (session == null)
            {
                _authFlow.ClearAuthCookies(httpContext);
                return new UnauthorizedObjectResult("Session expired.");
            }

            if (DemoUserPolicy.IsExpired(session.User, DateTime.UtcNow))
            {
                _context.UserSessions.Remove(session);
                await _context.SaveChangesAsync(cancellationToken);
                _authFlow.ClearAuthCookies(httpContext);
                return new UnauthorizedObjectResult("Session expired.");
            }

            var newRefresh = GenerateRefreshToken();
            session.RefreshTokenHash = _authFlow.HashRefresh(newRefresh);
            session.ExpiresAt = DateTime.UtcNow.AddDays(7);
            session.LastUsedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            _authFlow.SetRefreshCookie(httpContext, newRefresh, session.ExpiresAt);
            _authFlow.SetSessionHintCookie(httpContext, session);

            var newAccess = _authFlow.GenerateAccessToken(session.User);
            if (string.IsNullOrWhiteSpace(newAccess))
            {
                return new ObjectResult("Unable to complete sign-in right now.")
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

            return new OkObjectResult(new { accessToken = newAccess });
        }

        private static string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToHexString(randomNumber).ToLower();
        }
    }
}
