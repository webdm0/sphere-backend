using SphereBackend.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SphereBackend.Features.Auth
{
    public interface IConfirmEmailHandler
    {
        Task<IActionResult> HandleAsync(
            string token,
            HttpContext httpContext,
            CancellationToken cancellationToken = default);
    }

    public sealed class ConfirmEmailHandler : IConfirmEmailHandler
    {
        private readonly AppDbContext _context;
        private readonly IAuthFlowService _authFlow;

        public ConfirmEmailHandler(AppDbContext context, IAuthFlowService authFlow)
        {
            _context = context;
            _authFlow = authFlow;
        }

        public async Task<IActionResult> HandleAsync(
            string token,
            HttpContext httpContext,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(token))
            {
                return new BadRequestObjectResult("Invalid confirmation link.");
            }

            var user = await _context.Users
                .Include(u => u.Sessions)
                .FirstOrDefaultAsync(u => u.EmailConfirmationToken == token, cancellationToken);

            if (user == null)
            {
                return new BadRequestObjectResult("This link is invalid or has expired.");
            }

            var ipAddress = _authFlow.GetClientIp(httpContext);
            var cacheKey = $"reg_limit_{ipAddress}";

            if (user.EmailConfirmationToken != token || user.ConfirmationTokenExpiresAt < DateTime.UtcNow)
            {
                return new BadRequestObjectResult("This link is invalid or has expired.");
            }

            user.IsEmailConfirmed = true;
            user.EmailConfirmationToken = null;
            user.ConfirmationTokenExpiresAt = null;

            _authFlow.RemoveBanInfo(cacheKey);

            var accessToken = await _authFlow.CreateSessionAndGetTokenAsync(user, httpContext, cancellationToken);

            return new OkObjectResult(new { accessToken });
        }
    }
}
