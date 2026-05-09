using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Models;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Cards
{
    public interface IReorderCardsHandler
    {
        Task<IActionResult> HandleAsync(
            ReorderCardsRequestDto request,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class ReorderCardsHandler : IReorderCardsHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;

        public ReorderCardsHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher)
        {
            _context = context;
            _eventService = eventService;
            _idHasher = idHasher;
        }

        public async Task<IActionResult> HandleAsync(
            ReorderCardsRequestDto request,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (request == null || request.Cards == null || request.Cards.Count == 0)
            {
                return new BadRequestObjectResult("Invalid reorder request.");
            }

            if (request.Cards.Any(c => c == null))
            {
                return new BadRequestObjectResult("Invalid reorder request.");
            }

            var userId = user.GetCurrentUserId(_idHasher);

            if (!HashIdParser.TryDecode(_idHasher, request.TargetColumnId, out var targetColumnId))
            {
                return new BadRequestObjectResult("Invalid reorder request.");
            }

            var payloadCardIds = new List<int>();
            foreach (var item in request.Cards)
            {
                if (!HashIdParser.TryDecode(_idHasher, item.Id, out var parsedId))
                {
                    return new BadRequestObjectResult("Invalid reorder request.");
                }

                payloadCardIds.Add(parsedId);
            }

            if (CardFeatureHelpers.HasDuplicateIds(payloadCardIds))
            {
                return new BadRequestObjectResult("Invalid reorder request.");
            }

            var boardId = await _context.Columns
                .AsNoTracking()
                .Where(c => c.Id == targetColumnId)
                .Select(c => (int?)c.BoardId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!boardId.HasValue)
            {
                return new BadRequestObjectResult("Invalid reorder request.");
            }

            try
            {
                var changed = false;
                var result = await _context.ExecuteWithBoardOrderGuardAsync<IActionResult>(boardId.Value, async _ =>
                {
                    var targetColumn = await _context.Columns
                        .Include(c => c.Board)
                        .FirstOrDefaultAsync(c =>
                            c.Id == targetColumnId &&
                            c.ArchivedAt == null &&
                            (c.Board.UserId == userId ||
                             c.Board.Members.Any(m => m.UserId == userId && m.IsAccepted)),
                            cancellationToken);

                    if (targetColumn == null)
                    {
                        return new BadRequestObjectResult("Invalid reorder request.");
                    }

                    if (targetColumn.Board.ArchivedAt != null)
                    {
                        return new BadRequestObjectResult("Archived boards can't be changed.");
                    }

                    var currentTargetCards = await _context.Cards
                        .Where(c => c.ColumnId == targetColumnId && c.ArchivedAt == null)
                        .OrderBy(c => c.Order)
                        .ThenBy(c => c.Id)
                        .ToListAsync(cancellationToken);

                    var existingPayloadCards = await _context.Cards
                        .Where(c => payloadCardIds.Contains(c.Id))
                        .Select(c => new
                        {
                            c.Id,
                            c.BoardId,
                            c.ColumnId,
                            c.ArchivedAt
                        })
                        .ToListAsync(cancellationToken);

                    if (existingPayloadCards.Any(c => c.BoardId != targetColumn.BoardId))
                    {
                        return new BadRequestObjectResult("Invalid reorder request.");
                    }

                    var validPayloadCardIds = existingPayloadCards
                        .Where(c => c.BoardId == targetColumn.BoardId && c.ArchivedAt == null)
                        .Select(c => c.Id)
                        .ToList();

                    var cards = validPayloadCardIds.Count == 0
                        ? new List<Card>()
                        : await _context.Cards
                            .Where(c => validPayloadCardIds.Contains(c.Id))
                            .ToListAsync(cancellationToken);

                    if (cards.Any(c => c.ColumnId == null))
                    {
                        return new BadRequestObjectResult("Invalid reorder request.");
                    }

                    var cardsById = cards.ToDictionary(c => c.Id);
                    var reorderedTargetCards = payloadCardIds
                        .Where(cardsById.ContainsKey)
                        .Select(id => cardsById[id])
                        .ToList();

                    var includedCardIds = reorderedTargetCards
                        .Select(c => c.Id)
                        .ToHashSet();

                    reorderedTargetCards.AddRange(currentTargetCards.Where(c => !includedCardIds.Contains(c.Id)));

                    var movedCardIds = reorderedTargetCards
                        .Select(c => c.Id)
                        .ToHashSet();

                    var sourceColumnIds = reorderedTargetCards
                        .Where(c => c.ColumnId.HasValue && c.ColumnId.Value != targetColumnId)
                        .Select(c => c.ColumnId!.Value)
                        .Distinct()
                        .ToList();

                    var remainingSourceCards = sourceColumnIds.Count == 0
                        ? new List<Card>()
                        : await _context.Cards
                            .Where(c => c.ColumnId.HasValue &&
                                        sourceColumnIds.Contains(c.ColumnId.Value) &&
                                        c.ArchivedAt == null &&
                                        !movedCardIds.Contains(c.Id))
                            .OrderBy(c => c.Order)
                            .ThenBy(c => c.Id)
                            .ToListAsync(cancellationToken);

                    var now = DateTime.UtcNow;

                    foreach (var card in reorderedTargetCards)
                    {
                        card.ColumnId = targetColumnId;
                        card.UpdatedAt = now;
                        card.UpdatedById = userId;
                    }

                    CardFeatureHelpers.AssignSequentialOrders(reorderedTargetCards, now, userId);

                    foreach (var sourceCards in remainingSourceCards
                        .GroupBy(c => c.ColumnId!.Value)
                        .Select(group => group.OrderBy(c => c.Order).ThenBy(c => c.Id).ToList()))
                    {
                        CardFeatureHelpers.AssignSequentialOrders(sourceCards, now, userId);
                    }

                    await _context.SaveChangesAsync(cancellationToken);
                    changed = true;
                    return new NoContentResult();
                }, cancellationToken);

                if (changed)
                {
                    await _eventService.NotifyBoardAsync(boardId.Value, "CARDS_REORDERED");
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
                return new ObjectResult("Unable to reorder right now.")
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }
    }
}
