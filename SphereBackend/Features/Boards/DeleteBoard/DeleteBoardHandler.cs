using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface IDeleteBoardHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class DeleteBoardHandler : IDeleteBoardHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;

        public DeleteBoardHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher)
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

            if (!board.IsArchived)
            {
                return new BadRequestObjectResult("Only archived boards can be deleted.");
            }

            _context.Boards.Remove(board);
            await _context.SaveChangesAsync(cancellationToken);
            await _eventService.DisconnectBoardAsync(boardId);

            return new NoContentResult();
        }
    }
}
