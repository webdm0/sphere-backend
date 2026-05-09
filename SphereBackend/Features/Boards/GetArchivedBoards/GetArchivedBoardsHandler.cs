using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface IGetArchivedBoardsHandler
    {
        Task<IActionResult> HandleAsync(
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class GetArchivedBoardsHandler : IGetArchivedBoardsHandler
    {
        private readonly AppDbContext _context;
        private readonly IIdHasher _idHasher;

        public GetArchivedBoardsHandler(AppDbContext context, IIdHasher idHasher)
        {
            _context = context;
            _idHasher = idHasher;
        }

        public async Task<IActionResult> HandleAsync(
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            var userId = user.GetCurrentUserId(_idHasher);

            var boards = await _context.Boards
                .Where(b =>
                    b.ArchivedAt != null &&
                    (b.UserId == userId || b.Members.Any(m => m.UserId == userId && m.IsAccepted))
                )
                .Select(b => new
                {
                    b.Id,
                    b.Title,
                    b.UserId,
                    b.CreatedAt,
                    b.ArchivedAt,
                    OwnerName = b.User.Username,
                    MemberInfo = b.Members
                        .Where(m => m.UserId == userId)
                        .Select(m => new { m.IsAccepted, m.DateAdded })
                        .FirstOrDefault()
                })
                .OrderByDescending(b => b.ArchivedAt)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var result = boards.Select(b => new
            {
                Id = _idHasher.Encode(b.Id),
                b.Title,
                IsMine = b.UserId == userId,
                IsShared = b.UserId != userId && b.MemberInfo != null,
                IsAccepted = b.MemberInfo?.IsAccepted ?? false,
                b.OwnerName,
                ArchivedAt = b.ArchivedAt
            });

            return new OkObjectResult(result);
        }
    }
}
