using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Columns
{
    public interface IRestoreColumnHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class RestoreColumnHandler : IRestoreColumnHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;

        public RestoreColumnHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher)
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

            var boardId = await _context.Columns
                .AsNoTracking()
                .Where(c => c.Id == columnId)
                .Select(c => (int?)c.BoardId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!boardId.HasValue)
            {
                return new NotFoundObjectResult("Column not found.");
            }

            try
            {
                var restoredColumnId = 0;
                var result = await _context.ExecuteWithBoardOrderGuardAsync<IActionResult>(boardId.Value, async _ =>
                {
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
                        return new BadRequestObjectResult("This column isn't archived.");
                    }

                    if (column.Board.ArchivedAt != null)
                    {
                        return new BadRequestObjectResult("Archived boards can't be changed.");
                    }

                    column.ArchivedAt = null;

                    var activeColumns = await _context.Columns
                        .Where(c => c.BoardId == column.BoardId &&
                                    c.Id != column.Id &&
                                    c.ArchivedAt == null)
                        .OrderBy(c => c.Order)
                        .ThenBy(c => c.Id)
                        .ToListAsync(cancellationToken);

                    var insertIndex = Math.Clamp(column.Order, 0, activeColumns.Count);
                    activeColumns.Insert(insertIndex, column);
                    ColumnFeatureHelpers.AssignSequentialOrders(activeColumns);

                    var cardsToRestore = await _context.Cards
                        .Where(c => c.PreviousColumnId == columnId && !c.ArchivedManually && c.ArchivedAt != null)
                        .OrderBy(c => c.Order)
                        .ThenBy(c => c.Id)
                        .ToListAsync(cancellationToken);

                    for (var index = 0; index < cardsToRestore.Count; index++)
                    {
                        var card = cardsToRestore[index];
                        card.ColumnId = columnId;
                        card.PreviousColumnId = null;
                        card.ArchivedAt = null;
                        card.Order = index;
                        card.UpdatedAt = DateTime.UtcNow;
                        card.UpdatedById = userId;
                    }

                    await _context.SaveChangesAsync(cancellationToken);

                    var updatedColumns = await _context.Columns
                        .Where(c => c.BoardId == column.BoardId && c.ArchivedAt == null)
                        .OrderBy(c => c.Order)
                        .ThenBy(c => c.Id)
                        .Select(c => new
                        {
                            c.Id,
                            c.Title,
                            c.Order,
                            ArchivedAt = (DateTime?)null,
                            Cards = c.Cards
                                .Where(card => card.ArchivedAt == null && card.ArchivedManually == false)
                                .OrderBy(card => card.Order)
                                .ThenBy(card => card.Id)
                                .Select(card => new
                                {
                                    card.Id,
                                    card.Title,
                                    card.Order,
                                    card.ColumnId,
                                    card.AssigneeId,
                                    ArchivedAt = (DateTime?)null,
                                    IsArchived = false,
                                    ArchivedManually = false
                                }).ToList()
                        })
                        .ToListAsync(cancellationToken);

                    restoredColumnId = column.Id;
                    var response = updatedColumns.Select(c => new
                    {
                        Id = _idHasher.Encode(c.Id),
                        c.Title,
                        c.Order,
                        c.ArchivedAt,
                        Cards = c.Cards.Select(card => new
                        {
                            Id = _idHasher.Encode(card.Id),
                            card.Title,
                            card.Order,
                            ColumnId = card.ColumnId != null ? _idHasher.Encode(card.ColumnId.Value) : null,
                            AssigneeId = card.AssigneeId != null ? _idHasher.Encode(card.AssigneeId.Value) : null,
                            card.ArchivedAt,
                            card.IsArchived,
                            card.ArchivedManually
                        }).ToList()
                    });

                    return new OkObjectResult(response);
                }, cancellationToken);

                if (restoredColumnId != 0)
                {
                    await _eventService.NotifyBoardAsync(boardId.Value, "COLUMN_RESTORED", new { columnId = _idHasher.Encode(restoredColumnId) });
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
                return new ObjectResult("Unable to restore the column right now.")
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }
    }
}
