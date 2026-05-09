using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Columns
{
    public interface IReorderColumnsHandler
    {
        Task<IActionResult> HandleAsync(
            ReorderColumnsRequestDto request,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class ReorderColumnsHandler : IReorderColumnsHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;

        public ReorderColumnsHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher)
        {
            _context = context;
            _eventService = eventService;
            _idHasher = idHasher;
        }

        public async Task<IActionResult> HandleAsync(
            ReorderColumnsRequestDto request,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (request == null || request.Columns == null || request.Columns.Count == 0)
            {
                return new BadRequestObjectResult("Invalid reorder request.");
            }

            if (request.Columns.Any(c => c == null))
            {
                return new BadRequestObjectResult("Invalid reorder request.");
            }

            var userId = user.GetCurrentUserId(_idHasher);

            if (!HashIdParser.TryDecode(_idHasher, request.BoardId, out var boardId))
            {
                return new BadRequestObjectResult("Invalid reorder request.");
            }

            var payloadColumns = new List<(int Id, int Order, int Position)>();
            for (var index = 0; index < request.Columns.Count; index++)
            {
                var item = request.Columns[index];
                if (!HashIdParser.TryDecode(_idHasher, item.Id, out var parsedId))
                {
                    return new BadRequestObjectResult("Invalid reorder request.");
                }

                payloadColumns.Add((parsedId, item.Order, index));
            }

            if (ColumnFeatureHelpers.HasDuplicateIds(payloadColumns.Select(column => column.Id)))
            {
                return new BadRequestObjectResult("Invalid reorder request.");
            }

            try
            {
                var changed = false;
                var result = await _context.ExecuteWithBoardOrderGuardAsync<IActionResult>(boardId, async _ =>
                {
                    var board = await _context.Boards
                        .FirstOrDefaultAsync(b =>
                            b.Id == boardId &&
                            (b.UserId == userId || b.Members.Any(m => m.UserId == userId && m.IsAccepted)),
                            cancellationToken);

                    if (board == null)
                    {
                        return new NotFoundObjectResult("Board not found.");
                    }

                    if (board.ArchivedAt != null)
                    {
                        return new BadRequestObjectResult("Archived boards can't be changed.");
                    }

                    var activeColumns = await _context.Columns
                        .Where(c => c.BoardId == boardId && c.ArchivedAt == null)
                        .OrderBy(c => c.Order)
                        .ThenBy(c => c.Id)
                        .ToListAsync(cancellationToken);

                    var payloadColumnIds = payloadColumns
                        .Select(column => column.Id)
                        .ToList();

                    var existingPayloadColumns = await _context.Columns
                        .Where(c => payloadColumnIds.Contains(c.Id))
                        .Select(c => new { c.Id, c.BoardId, c.ArchivedAt })
                        .ToListAsync(cancellationToken);

                    if (existingPayloadColumns.Any(c => c.BoardId != boardId))
                    {
                        return new BadRequestObjectResult("Invalid reorder request.");
                    }

                    var activeColumnsById = activeColumns.ToDictionary(c => c.Id);
                    var reorderedColumns = payloadColumns
                        .OrderBy(column => column.Order)
                        .ThenBy(column => column.Position)
                        .Select(column => column.Id)
                        .Where(activeColumnsById.ContainsKey)
                        .Select(id => activeColumnsById[id])
                        .ToList();

                    var includedColumnIds = reorderedColumns
                        .Select(c => c.Id)
                        .ToHashSet();

                    reorderedColumns.AddRange(activeColumns.Where(c => !includedColumnIds.Contains(c.Id)));

                    var tempOrderBase =
                        (activeColumns.Count == 0 ? 0 : activeColumns.Max(c => c.Order)) +
                        activeColumns.Count +
                        1;

                    for (var index = 0; index < reorderedColumns.Count; index++)
                    {
                        reorderedColumns[index].Order = tempOrderBase + index;
                    }

                    await _context.SaveChangesAsync(cancellationToken);

                    ColumnFeatureHelpers.AssignSequentialOrders(reorderedColumns);

                    await _context.SaveChangesAsync(cancellationToken);
                    changed = true;
                    return new NoContentResult();
                }, cancellationToken);

                if (changed)
                {
                    await _eventService.NotifyBoardAsync(boardId, "COLUMNS_REORDERED");
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
