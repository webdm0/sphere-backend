using SphereBackend.Data;
using SphereBackend.Infrastructure.Http;
using SphereBackend.Models;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace SphereBackend.Features.Auth
{
    public interface ICreateDemoSessionHandler
    {
        Task<IActionResult> HandleAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken = default);
    }

    public sealed class CreateDemoSessionHandler : ICreateDemoSessionHandler
    {
        private readonly AppDbContext _context;
        private readonly IAuthFlowService _authFlow;
        private readonly IAppCleanupService _cleanupService;

        public CreateDemoSessionHandler(
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
            var ipAddress = _authFlow.GetClientIp(httpContext);
            var cacheKey = $"demo_limit_{ipAddress}";
            var banInfo = _authFlow.GetBanInfo(cacheKey);
            if (banInfo.BannedUntil.HasValue && banInfo.BannedUntil > DateTime.UtcNow)
            {
                return TooManyAttemptsResponse(banInfo, httpContext);
            }

            await _cleanupService.CleanupAsync(cancellationToken);

            User? demoUser = null;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                demoUser = CreateDemoUser();
                _context.Users.Add(demoUser);

                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    break;
                }
                catch (DbUpdateException ex) when (_authFlow.IsUniqueConstraintViolation(ex))
                {
                    _context.Entry(demoUser).State = EntityState.Detached;
                    demoUser = null;
                }
            }

            if (demoUser == null)
            {
                return new ObjectResult("Unable to create a demo session right now.")
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

            var accessToken = await _authFlow.CreateSessionAndGetTokenAsync(demoUser, httpContext, cancellationToken);
            _authFlow.RegisterAttempt(cacheKey, banInfo);

            return new OkObjectResult(new
            {
                accessToken,
                demoExpiresAtUtc = DemoUserPolicy.GetExpiresAtUtc(demoUser)
            });
        }

        private static User CreateDemoUser()
        {
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
            var username = $"demo_{suffix}";

            return new User
            {
                Username = username,
                Email = $"{username}@example.invalid",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(16))),
                IsEmailConfirmed = true,
                IsDemo = true,
                CreatedAt = DateTime.UtcNow
            };
        }

        private static ObjectResult TooManyAttemptsResponse(UserBanInfo banInfo, HttpContext httpContext)
        {
            var retryAfterUtc = banInfo.BannedUntil!.Value;
            var remaining = retryAfterUtc - DateTime.UtcNow;
            var problem = ApiProblemDetailsFactory.CreateTooManyRequests(
                $"Too many demo sessions created. Please wait {Math.Ceiling(remaining.TotalMinutes)} minutes.",
                retryAfterUtc,
                httpContext);

            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status429TooManyRequests
            };
        }
    }
}
