using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Cards
{
    public interface IDeleteCardForeverHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class DeleteCardForeverHandler : IDeleteCardForeverHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;

        public DeleteCardForeverHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher)
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
            if (!HashIdParser.TryDecode(_idHasher, id, out var cardId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var userId = user.GetCurrentUserId(_idHasher);

            var card = await _context.Cards
                .Include(c => c.Board)
                .FirstOrDefaultAsync(c =>
                    c.Id == cardId &&
                    (c.Board.UserId == userId ||
                     c.Board.Members.Any(m => m.UserId == userId && m.IsAccepted)),
                    cancellationToken);

            if (card == null)
            {
                return new NotFoundObjectResult("Card not found.");
            }

            if (card.ArchivedAt == null)
            {
                return new BadRequestObjectResult("Archive this card before deleting it permanently.");
            }

            var board = card.Board;
            if (board == null)
            {
                return new ObjectResult("An unexpected error occurred.")
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

            if (board.ArchivedAt != null)
            {
                return new BadRequestObjectResult("Archived boards can't be changed.");
            }

            _context.Cards.Remove(card);
            await _context.SaveChangesAsync(cancellationToken);

            await _eventService.NotifyBoardAsync(board.Id, "CARD_DELETED", new { cardId = _idHasher.Encode(cardId) });

            return new NoContentResult();
        }
    }
}
