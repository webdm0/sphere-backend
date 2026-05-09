using SphereBackend.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SphereBackend.Features.Auth
{
    public interface IValidateSessionHandler
    {
        Task<IActionResult> HandleAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken = default);
    }

    public sealed class ValidateSessionHandler : IValidateSessionHandler
    {
        private readonly AppDbContext _context;
        private readonly IAuthFlowService _authFlow;

        public ValidateSessionHandler(AppDbContext context, IAuthFlowService authFlow)
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
                .AsNoTracking()
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
                _authFlow.ClearAuthCookies(httpContext);
                return new UnauthorizedObjectResult("Session expired.");
            }

            _authFlow.SetSessionHintCookie(httpContext, session);

            return new NoContentResult();
        }
    }
}
