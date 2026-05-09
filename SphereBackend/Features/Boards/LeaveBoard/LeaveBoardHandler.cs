using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface ILeaveBoardHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class LeaveBoardHandler : ILeaveBoardHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;

        public LeaveBoardHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher)
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
                .Where(b =>
                    b.Id == boardId &&
                    (b.UserId == userId || b.Members.Any(m => m.UserId == userId)))
                .Select(b => new
                {
                    b.Id,
                    b.UserId
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (board == null)
            {
                return new NotFoundObjectResult("Board not found.");
            }

            if (board.UserId == userId)
            {
                return new BadRequestObjectResult("Board owners can't leave the board. Transfer ownership or delete the board first.");
            }

            var member = await _context.BoardMembers
                .FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == userId, cancellationToken);
            if (member == null)
            {
                return new NotFoundObjectResult("Board not found.");
            }

            _context.BoardMembers.Remove(member);
            await _context.SaveChangesAsync(cancellationToken);

            await _eventService.NotifyBoardAsync(boardId, "MEMBER_REMOVED");
            await _eventService.DisconnectUserAsync(boardId, userId);

            return new NoContentResult();
        }
    }
}
