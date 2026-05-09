using AutoMapper;
using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Models;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Cards
{
    public interface IRestoreCardHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class RestoreCardHandler : IRestoreCardHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;
        private readonly IMapper _mapper;

        public RestoreCardHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher, IMapper mapper)
        {
            _context = context;
            _eventService = eventService;
            _idHasher = idHasher;
            _mapper = mapper;
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

            var boardId = await _context.Cards
                .AsNoTracking()
                .Where(c => c.Id == cardId)
                .Select(c => (int?)c.BoardId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!boardId.HasValue)
            {
                return new NotFoundObjectResult("Card not found.");
            }

            try
            {
                var restoredCardId = 0;
                var result = await _context.ExecuteWithBoardOrderGuardAsync<IActionResult>(boardId.Value, async _ =>
                {
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
                        return new BadRequestObjectResult("This card isn't archived.");
                    }

                    var board = card.Board;

                    if (board.ArchivedAt != null)
                    {
                        return new BadRequestObjectResult("Archived boards can't be changed.");
                    }

                    string restoreContext;
                    Column? targetColumn = null;

                    if (card.PreviousColumnId.HasValue)
                    {
                        targetColumn = await _context.Columns
                            .FirstOrDefaultAsync(c => c.Id == card.PreviousColumnId.Value && c.BoardId == board.Id, cancellationToken);
                    }

                    if (targetColumn == null)
                    {
                        restoreContext = "original_deleted";
                        targetColumn = await GetFallbackColumn(board.Id, cancellationToken);
                    }
                    else if (targetColumn.ArchivedAt != null)
                    {
                        restoreContext = "original_archived";
                        targetColumn = await GetFallbackColumn(board.Id, cancellationToken);
                    }
                    else
                    {
                        restoreContext = "original";
                    }

                    if (targetColumn == null)
                    {
                        return new OkObjectResult(new RestoreCardResponseDto
                        {
                            Card = null,
                            RestoreContext = "no_columns"
                        });
                    }

                    var now = DateTime.UtcNow;
                    var targetCards = await _context.Cards
                        .Where(c => c.ColumnId == targetColumn.Id &&
                                    c.ArchivedAt == null &&
                                    c.Id != card.Id)
                        .OrderBy(c => c.Order)
                        .ThenBy(c => c.Id)
                        .ToListAsync(cancellationToken);

                    card.ColumnId = targetColumn.Id;
                    card.BoardId = targetColumn.BoardId;
                    card.PreviousColumnId = null;
                    card.ArchivedAt = null;
                    card.ArchivedManually = false;
                    card.UpdatedAt = now;
                    card.UpdatedById = userId;

                    var insertIndex = Math.Clamp(card.Order, 0, targetCards.Count);
                    targetCards.Insert(insertIndex, card);
                    CardFeatureHelpers.AssignSequentialOrders(targetCards, now, userId);

                    await _context.SaveChangesAsync(cancellationToken);

                    restoredCardId = card.Id;
                    return new OkObjectResult(new RestoreCardResponseDto
                    {
                        Card = CardFeatureHelpers.MapToDto(_mapper, card),
                        RestoreContext = restoreContext,
                        TargetColumn = CardFeatureHelpers.MapTargetColumnDto(_idHasher, targetColumn, targetCards)
                    });
                }, cancellationToken);

                if (restoredCardId != 0)
                {
                    await _eventService.NotifyBoardAsync(boardId.Value, "CARD_RESTORED", new { cardId = _idHasher.Encode(restoredCardId) });
                }

                return result;
            }
            catch (DbUpdateException ex) when (ex.IsOrderUniquenessViolation())
            {
                return new ObjectResult("Board order changed. Please retry.")
                {
                    StatusCode = StatusCodes.Status409Conflict
                };
            }
            catch (Exception)
            {
                return new ObjectResult("Unable to restore the card right now.")
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        private async Task<Column?> GetFallbackColumn(int boardId, CancellationToken cancellationToken)
        {
            return await _context.Columns
                .Where(c => c.BoardId == boardId && c.ArchivedAt == null)
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
