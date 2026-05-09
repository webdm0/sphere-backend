using SphereBackend.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Resend;

namespace SphereBackend.Features.Auth
{
    public interface IResendConfirmationHandler
    {
        Task<IActionResult> HandleAsync(
            ResendConfirmationRequest request,
            CancellationToken cancellationToken = default);
    }

    public sealed class ResendConfirmationHandler : IResendConfirmationHandler
    {
        private readonly AppDbContext _context;
        private readonly IAuthFlowService _authFlow;
        private readonly IResend _resend;

        public ResendConfirmationHandler(AppDbContext context, IAuthFlowService authFlow, IResend resend)
        {
            _context = context;
            _authFlow = authFlow;
            _resend = resend;
        }

        public async Task<IActionResult> HandleAsync(
            ResendConfirmationRequest request,
            CancellationToken cancellationToken = default)
        {
            var normalizedEmailLookup = _authFlow.NormalizeForLookup(request.Email);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmailLookup, cancellationToken);

            if (user == null || user.IsEmailConfirmed)
            {
                return new OkObjectResult(new { message = _authFlow.GenericResendResponseMessage });
            }

            if (_authFlow.IsConfirmationEmailCooldownActive(user))
            {
                return new OkObjectResult(new { message = _authFlow.GenericResendResponseMessage });
            }

            var newToken = Guid.NewGuid().ToString();
            user.EmailConfirmationToken = newToken;
            user.ConfirmationTokenExpiresAt = DateTime.UtcNow.AddHours(2);
            _authFlow.MarkConfirmationEmailSent(user);

            await _context.SaveChangesAsync(cancellationToken);

            await _authFlow.SendConfirmationEmailAsync(user, newToken, _resend);

            return new OkObjectResult(new { message = _authFlow.GenericResendResponseMessage });
        }
    }
}
