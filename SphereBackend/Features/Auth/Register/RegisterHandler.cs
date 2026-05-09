using SphereBackend.Data;
using SphereBackend.Infrastructure.Http;
using SphereBackend.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Resend;

namespace SphereBackend.Features.Auth
{
    public interface IRegisterHandler
    {
        Task<IActionResult> HandleAsync(
            RegisterRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken = default);
    }

    public sealed class RegisterHandler : IRegisterHandler
    {
        private readonly AppDbContext _context;
        private readonly IAuthFlowService _authFlow;
        private readonly IResend _resend;

        public RegisterHandler(AppDbContext context, IAuthFlowService authFlow, IResend resend)
        {
            _context = context;
            _authFlow = authFlow;
            _resend = resend;
        }

        public async Task<IActionResult> HandleAsync(
            RegisterRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var ipAddress = _authFlow.GetClientIp(httpContext);
            var cacheKey = $"reg_limit_{ipAddress}";
            var normalizedUsername = _authFlow.NormalizeForStorage(request.Username);
            var normalizedUsernameLookup = _authFlow.NormalizeForLookup(request.Username);
            var normalizedEmail = _authFlow.NormalizeForStorage(request.Email);
            var normalizedEmailLookup = _authFlow.NormalizeForLookup(request.Email);

            var banInfo = _authFlow.GetBanInfo(cacheKey);

            if (banInfo.BannedUntil.HasValue && banInfo.BannedUntil > DateTime.UtcNow)
            {
                return TooManyAttemptsResponse(banInfo, "Too many attempts. Please wait {0} minutes.", httpContext);
            }

            var userWithSameName = await _context.Users
                .FirstOrDefaultAsync(u => u.NormalizedUsername == normalizedUsernameLookup, cancellationToken);

            if (userWithSameName != null && userWithSameName.NormalizedEmail != normalizedEmailLookup)
            {
                _authFlow.RegisterAttempt(cacheKey, banInfo);

                if (banInfo.BannedUntil.HasValue && banInfo.BannedUntil > DateTime.UtcNow)
                {
                    return TooManyAttemptsResponse(banInfo, "Too many attempts. Please wait {0} minutes.", httpContext);
                }

                return new OkObjectResult(new { message = _authFlow.GenericRegisterResponseMessage });
            }

            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmailLookup, cancellationToken);

            if (existingUser != null)
            {
                if (existingUser.IsEmailConfirmed)
                {
                    _authFlow.RegisterAttempt(cacheKey, banInfo);

                    if (banInfo.BannedUntil.HasValue && banInfo.BannedUntil > DateTime.UtcNow)
                    {
                        return TooManyAttemptsResponse(banInfo, "Too many attempts. Please wait {0} minutes.", httpContext);
                    }

                    return new OkObjectResult(new { message = _authFlow.GenericRegisterResponseMessage });
                }

                _authFlow.RegisterAttempt(cacheKey, banInfo);

                if (banInfo.BannedUntil.HasValue && banInfo.BannedUntil > DateTime.UtcNow)
                {
                    return TooManyAttemptsResponse(banInfo, "Too many attempts. Registration is blocked for {0} minutes.", httpContext);
                }

                existingUser.Username = normalizedUsername;
                existingUser.Email = normalizedEmail;
                existingUser.NormalizedUsername = normalizedUsernameLookup;
                existingUser.NormalizedEmail = normalizedEmailLookup;
                existingUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
                var canReuseExistingConfirmationLink =
                    !string.IsNullOrWhiteSpace(existingUser.EmailConfirmationToken) &&
                    existingUser.ConfirmationTokenExpiresAt.HasValue &&
                    existingUser.ConfirmationTokenExpiresAt.Value > now;
                var shouldSendConfirmationEmail =
                    !canReuseExistingConfirmationLink ||
                    !_authFlow.IsConfirmationEmailCooldownActive(existingUser);
                string? newToken = null;

                if (shouldSendConfirmationEmail)
                {
                    newToken = Guid.NewGuid().ToString();
                    existingUser.EmailConfirmationToken = newToken;
                    existingUser.ConfirmationTokenExpiresAt = now.AddHours(2);
                    _authFlow.MarkConfirmationEmailSent(existingUser);
                }

                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (_authFlow.IsUniqueConstraintViolation(ex))
                {
                    return new OkObjectResult(new { message = _authFlow.GenericRegisterResponseMessage });
                }

                if (shouldSendConfirmationEmail && newToken != null)
                {
                    await _authFlow.SendConfirmationEmailAsync(existingUser, newToken, _resend);
                }

                return new OkObjectResult(new { message = _authFlow.GenericRegisterResponseMessage });
            }

            _authFlow.RegisterAttempt(cacheKey, banInfo);

            if (banInfo.BannedUntil.HasValue && banInfo.BannedUntil > DateTime.UtcNow)
            {
                return TooManyAttemptsResponse(banInfo, "Too many attempts. Please wait {0} minutes.", httpContext);
            }

            var confirmationToken = Guid.NewGuid().ToString();
            var user = new User
            {
                Username = normalizedUsername,
                Email = normalizedEmail,
                NormalizedUsername = normalizedUsernameLookup,
                NormalizedEmail = normalizedEmailLookup,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                IsEmailConfirmed = false,
                EmailConfirmationToken = confirmationToken,
                ConfirmationTokenExpiresAt = now.AddHours(2),
                LastConfirmationEmailSentAt = now
            };

            _context.Users.Add(user);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (_authFlow.IsUniqueConstraintViolation(ex))
            {
                return new OkObjectResult(new { message = _authFlow.GenericRegisterResponseMessage });
            }

            await _authFlow.SendConfirmationEmailAsync(user, confirmationToken, _resend);

            return new OkObjectResult(new { message = _authFlow.GenericRegisterResponseMessage });
        }

        private static ObjectResult TooManyAttemptsResponse(
            UserBanInfo banInfo,
            string template,
            HttpContext httpContext)
        {
            var retryAfterUtc = banInfo.BannedUntil!.Value;
            var remaining = retryAfterUtc - DateTime.UtcNow;
            var problem = ApiProblemDetailsFactory.CreateTooManyRequests(
                string.Format(template, Math.Ceiling(remaining.TotalMinutes)),
                retryAfterUtc,
                httpContext);

            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status429TooManyRequests
            };
        }
    }
}
