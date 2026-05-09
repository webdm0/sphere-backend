using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface IUpdateBoardHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            UpdateBoardDto dto,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class UpdateBoardHandler : IUpdateBoardHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;

        public UpdateBoardHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher)
        {
            _context = context;
            _eventService = eventService;
            _idHasher = idHasher;
        }

        public async Task<IActionResult> HandleAsync(
            string id,
            UpdateBoardDto dto,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (!HashIdParser.TryDecode(_idHasher, id, out var boardId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var normalizedTitle = dto.Title.Trim();
            if (normalizedTitle.Length == 0)
            {
                return new BadRequestObjectResult("Title is required.");
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
                return new BadRequestObjectResult("Archived boards can't be changed.");
            }

            board.Title = normalizedTitle;
            await _context.SaveChangesAsync(cancellationToken);

            await _eventService.NotifyBoardAsync(boardId, "BOARD_UPDATED", new { title = board.Title });

            return new OkObjectResult(new { Id = _idHasher.Encode(board.Id), board.Title });
        }
    }
}
