using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface IArchiveBoardHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class ArchiveBoardHandler : IArchiveBoardHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;

        public ArchiveBoardHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher)
        {
            _context = context;
            _eventService = eventService;
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
                .FirstOrDefaultAsync(b => b.Id == boardId && b.UserId == userId, cancellationToken);

            if (board == null)
            {
                return new NotFoundObjectResult("Board not found.");
            }

            if (board.IsArchived)
            {
                return new BadRequestObjectResult("This board is already archived.");
            }

            var pending = await _context.BoardMembers
               .Where(m => m.BoardId == boardId && !m.IsAccepted)
               .ToListAsync(cancellationToken);

            _context.BoardMembers.RemoveRange(pending);

            board.ArchivedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            await _eventService.NotifyBoardAsync(boardId, "BOARD_ARCHIVED");

            return new OkResult();
        }
    }
}
