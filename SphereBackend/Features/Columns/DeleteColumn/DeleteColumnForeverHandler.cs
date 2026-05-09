using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Columns
{
    public interface IDeleteColumnForeverHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class DeleteColumnForeverHandler : IDeleteColumnForeverHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;

        public DeleteColumnForeverHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher)
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
            if (!HashIdParser.TryDecode(_idHasher, id, out var columnId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var userId = user.GetCurrentUserId(_idHasher);

            var column = await _context.Columns
                .Include(c => c.Board)
                .FirstOrDefaultAsync(c =>
                    c.Id == columnId &&
                    (c.Board.UserId == userId || c.Board.Members.Any(m => m.UserId == userId && m.IsAccepted)),
                    cancellationToken);

            if (column == null)
            {
                return new NotFoundObjectResult("Column not found.");
            }

            if (column.ArchivedAt == null)
            {
                return new BadRequestObjectResult("Archive this column before deleting it permanently.");
            }

            if (column.Board.ArchivedAt != null)
            {
                return new BadRequestObjectResult("Archived boards can't be changed.");
            }

            var cards = await _context.Cards
                .Where(c => c.PreviousColumnId == columnId && !c.ArchivedManually && c.BoardId == column.BoardId)
                .ToListAsync(cancellationToken);

            _context.Cards.RemoveRange(cards);
            _context.Columns.Remove(column);

            await _context.SaveChangesAsync(cancellationToken);

            await _eventService.NotifyBoardAsync(column.BoardId, "COLUMN_DELETED", new { columnId = _idHasher.Encode(columnId) });

            return new NoContentResult();
        }
    }
}
