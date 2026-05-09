using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Columns
{
    public interface IUpdateColumnHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            UpdateColumnDto dto,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class UpdateColumnHandler : IUpdateColumnHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;

        public UpdateColumnHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher)
        {
            _context = context;
            _eventService = eventService;
            _idHasher = idHasher;
        }

        public async Task<IActionResult> HandleAsync(
            string id,
            UpdateColumnDto dto,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (!HashIdParser.TryDecode(_idHasher, id, out var columnId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var userId = user.GetCurrentUserId(_idHasher);

            if (!dto.IsArchivedSpecified || dto.IsArchived != true)
            {
                var column = await _context.Columns
                    .Include(c => c.Board)
                    .FirstOrDefaultAsync(c =>
                        c.Id == columnId &&
                        c.ArchivedAt == null &&
                        (c.Board.UserId == userId || c.Board.Members.Any(m => m.UserId == userId && m.IsAccepted)),
                        cancellationToken);

                if (column == null)
                {
                    return new NotFoundObjectResult("Column not found.");
                }

                if (column.Board.ArchivedAt != null)
                {
                    return new BadRequestObjectResult("Archived boards can't be changed.");
                }

                if (dto.TitleSpecified)
                {
                    if (string.IsNullOrWhiteSpace(dto.Title))
                    {
                        return new BadRequestObjectResult("Title is required.");
                    }

                    column.Title = dto.Title.Trim();
                }

                if (dto.IsArchivedSpecified && dto.IsArchived != true)
                {
                    return new BadRequestObjectResult("Use restore to unarchive this column.");
                }

                await _context.SaveChangesAsync(cancellationToken);

                await _eventService.NotifyBoardAsync(column.BoardId, "COLUMN_UPDATED", new { columnId = _idHasher.Encode(column.Id) });

                return new OkObjectResult(new { Id = _idHasher.Encode(column.Id), column.Title, column.ArchivedAt });
            }

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
                var updatedColumnId = 0;
                var result = await _context.ExecuteWithBoardOrderGuardAsync<IActionResult>(boardId.Value, async _ =>
                {
                    var column = await _context.Columns
                        .Include(c => c.Board)
                        .Include(c => c.Cards)
                        .FirstOrDefaultAsync(c =>
                            c.Id == columnId &&
                            c.ArchivedAt == null &&
                            (c.Board.UserId == userId || c.Board.Members.Any(m => m.UserId == userId && m.IsAccepted)),
                            cancellationToken);

                    if (column == null)
                    {
                        return new NotFoundObjectResult("Column not found.");
                    }

                    if (column.Board.ArchivedAt != null)
                    {
                        return new BadRequestObjectResult("Archived boards can't be changed.");
                    }

                    if (dto.TitleSpecified)
                    {
                        if (string.IsNullOrWhiteSpace(dto.Title))
                        {
                            return new BadRequestObjectResult("Title is required.");
                        }

                        column.Title = dto.Title.Trim();
                    }

                    column.ArchivedAt = DateTime.UtcNow;

                    foreach (var card in column.Cards.Where(c => c.ArchivedAt == null))
                    {
                        card.PreviousColumnId = card.ColumnId;
                        card.ColumnId = null;
                        card.ArchivedAt = DateTime.UtcNow;
                        card.ArchivedManually = false;
                    }

                    var remainingColumns = await _context.Columns
                        .Where(c => c.BoardId == column.BoardId &&
                                    c.Id != column.Id &&
                                    c.ArchivedAt == null)
                        .OrderBy(c => c.Order)
                        .ThenBy(c => c.Id)
                        .ToListAsync(cancellationToken);

                    ColumnFeatureHelpers.AssignSequentialOrders(remainingColumns);
                    await _context.SaveChangesAsync(cancellationToken);

                    updatedColumnId = column.Id;
                    return new OkObjectResult(new { Id = _idHasher.Encode(column.Id), column.Title, column.ArchivedAt });
                }, cancellationToken);

                if (updatedColumnId != 0)
                {
                    await _eventService.NotifyBoardAsync(boardId.Value, "COLUMN_UPDATED", new { columnId = _idHasher.Encode(updatedColumnId) });
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
        }
    }
}
