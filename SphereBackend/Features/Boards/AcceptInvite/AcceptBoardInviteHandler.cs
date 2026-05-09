using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface IAcceptBoardInviteHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class AcceptBoardInviteHandler : IAcceptBoardInviteHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;

        public AcceptBoardInviteHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher)
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

            var member = await _context.BoardMembers
                .Include(m => m.Board)
                .FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == userId, cancellationToken);

            if (member == null)
            {
                return new OkResult();
            }

            if (member.Board.ArchivedAt != null)
            {
                return new OkResult();
            }

            if (member.IsAccepted)
            {
                return new OkResult();
            }

            member.IsAccepted = true;
            await _context.SaveChangesAsync(cancellationToken);

            await _eventService.NotifyBoardAsync(boardId, "MEMBER_JOINED");

            return new OkResult();
        }
    }
}
