using SphereBackend.Data;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SphereBackend.Features.Auth
{
    public interface ILogoutHandler
    {
        Task<IActionResult> HandleAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken = default);
    }

    public sealed class LogoutHandler : ILogoutHandler
    {
        private readonly AppDbContext _context;
        private readonly IAuthFlowService _authFlow;
        private readonly IAppCleanupService _cleanupService;

        public LogoutHandler(
            AppDbContext context,
            IAuthFlowService authFlow,
            IAppCleanupService cleanupService)
        {
            _context = context;
            _authFlow = authFlow;
            _cleanupService = cleanupService;
        }

        public async Task<IActionResult> HandleAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken = default)
        {
            var refreshToken = _authFlow.ReadRefreshTokenFromCookie(httpContext);
            if (!string.IsNullOrEmpty(refreshToken))
            {
                var refreshHash = _authFlow.HashRefresh(refreshToken);

                var session = await _context.UserSessions
                    .Include(s => s.User)
                    .FirstOrDefaultAsync(s => s.RefreshTokenHash == refreshHash, cancellationToken);

                if (session != null)
                {
                    if (session.User?.IsDemo == true)
                    {
                        await _cleanupService.DeleteDemoUserAsync(session.UserId, cancellationToken);
                    }
                    else
                    {
                        _context.UserSessions.Remove(session);
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                }
            }

            _authFlow.ClearAuthCookies(httpContext);

            return new OkResult();
        }
    }
}
