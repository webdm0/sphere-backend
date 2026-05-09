using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface IGetBoardsHandler
    {
        Task<IActionResult> HandleAsync(
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class GetBoardsHandler : IGetBoardsHandler
    {
        private readonly AppDbContext _context;
        private readonly IIdHasher _idHasher;

        public GetBoardsHandler(AppDbContext context, IIdHasher idHasher)
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
                .Where(b => (b.UserId == userId || b.Members.Any(m => m.UserId == userId)) && b.ArchivedAt == null)
                .Select(b => new
                {
                    b.Id,
                    b.Title,
                    b.UserId,
                    b.CreatedAt,
                    OwnerName = b.User.Username,
                    MemberInfo = b.Members
                        .Where(m => m.UserId == userId)
                        .Select(m => new { m.IsAccepted, m.DateAdded, m.Order })
                        .FirstOrDefault()
                })
                .OrderBy(b => b.MemberInfo != null ? b.MemberInfo.Order : int.MaxValue)
                .ThenBy(b => b.Id)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var result = boards.Select(b => new
            {
                Id = _idHasher.Encode(b.Id),
                b.Title,
                IsMine = b.UserId == userId,
                IsShared = b.UserId != userId && b.MemberInfo != null,
                IsAccepted = b.MemberInfo?.IsAccepted ?? false,
                b.OwnerName
            });

            return new OkObjectResult(result);
        }
    }
}
