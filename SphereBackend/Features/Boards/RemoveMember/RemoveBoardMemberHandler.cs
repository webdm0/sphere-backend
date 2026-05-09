using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface IRemoveBoardMemberHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            string userId,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class RemoveBoardMemberHandler : IRemoveBoardMemberHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;

        public RemoveBoardMemberHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher)
        {
            _context = context;
            _eventService = eventService;
            _idHasher = idHasher;
        }

        public async Task<IActionResult> HandleAsync(
            string id,
            string userId,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (!HashIdParser.TryDecode(_idHasher, id, out var boardId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            if (!HashIdParser.TryDecode(_idHasher, userId, out var memberUserId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var currentUserId = user.GetCurrentUserId(_idHasher);

            var board = await _context.Boards
                .FirstOrDefaultAsync(b => b.Id == boardId && b.UserId == currentUserId, cancellationToken);

            if (board == null)
            {
                return new NotFoundObjectResult("Board not found.");
            }

            if (board.ArchivedAt != null)
            {
                return new BadRequestObjectResult("Archived boards can't be changed.");
            }

            if (memberUserId == board.UserId)
            {
                return new BadRequestObjectResult("The board owner can't be removed.");
            }

            var member = await _context.BoardMembers
                .FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == memberUserId, cancellationToken);

            if (member == null)
            {
                return new NotFoundObjectResult("Member not found.");
            }

            _context.BoardMembers.Remove(member);
            await _context.SaveChangesAsync(cancellationToken);

            await _eventService.NotifyBoardAsync(boardId, "MEMBER_REMOVED");
            await _eventService.DisconnectUserAsync(boardId, memberUserId);

            return new NoContentResult();
        }
    }
}
