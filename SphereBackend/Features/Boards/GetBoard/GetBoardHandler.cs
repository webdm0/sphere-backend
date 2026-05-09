using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface IGetBoardHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class GetBoardHandler : IGetBoardHandler
    {
        private readonly AppDbContext _context;
        private readonly IIdHasher _idHasher;

        public GetBoardHandler(AppDbContext context, IIdHasher idHasher)
        {
            _context = context;
            _idHasher = idHasher;
        }

        public async Task<IActionResult> HandleAsync(
            string id,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (!HashIdParser.TryDecode(_idHasher, id, out var boardId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var userId = user.GetCurrentUserId(_idHasher);

            var board = await _context.Boards
                .Where(b => b.Id == boardId &&
                       (b.UserId == userId ||
                        b.Members.Any(m => m.UserId == userId && m.IsAccepted)))
                .Select(b => new
                {
                    b.Id,
                    b.Title,
                    IsArchived = b.ArchivedAt != null
                })
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (board == null)
            {
                return new NotFoundObjectResult("Board not found.");
            }

            return new OkObjectResult(new BoardShortDto
            {
                Id = _idHasher.Encode(board.Id),
                Title = board.Title,
                IsArchived = board.IsArchived
            });
        }
    }
}
