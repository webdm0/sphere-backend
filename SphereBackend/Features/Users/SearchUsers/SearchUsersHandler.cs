using SphereBackend.Data;
using SphereBackend.Services;
using Microsoft.EntityFrameworkCore;

namespace SphereBackend.Features.Users.SearchUsers
{
    public sealed class SearchUsersHandler : ISearchUsersHandler
    {
        private readonly AppDbContext _context;
        private readonly IIdHasher _idHasher;

        public SearchUsersHandler(AppDbContext context, IIdHasher idHasher)
        {
            _context = context;
            _idHasher = idHasher;
        }

        public async Task<IReadOnlyList<SearchUserItem>> HandleAsync(
            SearchUsersQuery query,
            CancellationToken cancellationToken = default)
        {
            var searchText = query.SearchText?.Trim();
            if (string.IsNullOrWhiteSpace(searchText) || searchText.Length < 2)
            {
                return Array.Empty<SearchUserItem>();
            }

            var escapedQuery = EscapeLikePattern(searchText);

            var users = await _context.Users
                .AsNoTracking()
                .Where(u => u.IsEmailConfirmed &&
                            !u.IsDemo &&
                            u.Id != query.CurrentUserId &&
                            EF.Functions.ILike(u.Username, $"%{escapedQuery}%"))
                .Select(u => new
                {
                    u.Id,
                    u.Username
                })
                .Take(10)
                .ToListAsync(cancellationToken);

            return users
                .Select(u => new SearchUserItem
                {
                    Id = _idHasher.Encode(u.Id),
                    Username = u.Username
                })
                .ToList();
        }

        private static string EscapeLikePattern(string input)
        {
            return input
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
        }
    }
}
