using SphereBackend.Data;
using SphereBackend.Features.Auth;
using Microsoft.EntityFrameworkCore;

namespace SphereBackend.Services
{
    public interface IAppCleanupService
    {
        Task CleanupAsync(CancellationToken cancellationToken = default);
        Task<int> CleanupExpiredDemoUsersAsync(CancellationToken cancellationToken = default);
        Task<int> CleanupExpiredUnconfirmedUsersAsync(CancellationToken cancellationToken = default);
        Task<bool> DeleteDemoUserAsync(int userId, CancellationToken cancellationToken = default);
    }

    public sealed class AppCleanupService : IAppCleanupService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<AppCleanupService> _logger;

        public AppCleanupService(AppDbContext context, ILogger<AppCleanupService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task CleanupAsync(CancellationToken cancellationToken = default)
        {
            await CleanupExpiredDemoUsersAsync(cancellationToken);
            await CleanupExpiredUnconfirmedUsersAsync(cancellationToken);
        }

        public async Task<int> CleanupExpiredDemoUsersAsync(CancellationToken cancellationToken = default)
        {
            var cutoff = DateTime.UtcNow - DemoUserPolicy.Lifetime;

            var expiredDemoUserIds = await _context.Users
                .Where(u => u.IsDemo && u.CreatedAt <= cutoff)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            if (expiredDemoUserIds.Count == 0)
            {
                return 0;
            }

            var deletedCount = await DeleteDemoUsersAsync(expiredDemoUserIds, cancellationToken);

            _logger.LogInformation("Deleted {Count} expired demo accounts.", deletedCount);
            return deletedCount;
        }

        public async Task<int> CleanupExpiredUnconfirmedUsersAsync(CancellationToken cancellationToken = default)
        {
            var expiredUsers = await _context.Users
                .Where(u => !u.IsEmailConfirmed && u.ConfirmationTokenExpiresAt < DateTime.UtcNow)
                .ToListAsync(cancellationToken);

            if (expiredUsers.Count == 0)
            {
                return 0;
            }

            _context.Users.RemoveRange(expiredUsers);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted {Count} unverified accounts.", expiredUsers.Count);
            return expiredUsers.Count;
        }

        public async Task<bool> DeleteDemoUserAsync(int userId, CancellationToken cancellationToken = default)
        {
            var deletedCount = await DeleteDemoUsersAsync(new[] { userId }, cancellationToken);

            if (deletedCount > 0)
            {
                _logger.LogInformation("Deleted demo account {UserId}.", userId);
                return true;
            }

            return false;
        }

        private async Task<int> DeleteDemoUsersAsync(
            IReadOnlyCollection<int> userIds,
            CancellationToken cancellationToken)
        {
            if (userIds.Count == 0)
            {
                return 0;
            }

            var demoUsers = await _context.Users
                .Where(u => u.IsDemo && userIds.Contains(u.Id))
                .ToListAsync(cancellationToken);

            if (demoUsers.Count == 0)
            {
                return 0;
            }

            var demoUserIds = demoUsers
                .Select(u => u.Id)
                .ToList();

            var cardsToUnassign = await _context.Cards
                .Where(c => c.AssigneeId.HasValue && demoUserIds.Contains(c.AssigneeId.Value))
                .ToListAsync(cancellationToken);

            foreach (var card in cardsToUnassign)
            {
                card.AssigneeId = null;
            }

            _context.Users.RemoveRange(demoUsers);
            await _context.SaveChangesAsync(cancellationToken);
            return demoUsers.Count;
        }
    }
}
