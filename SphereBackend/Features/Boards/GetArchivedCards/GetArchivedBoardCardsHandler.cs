using SphereBackend.Data;
using SphereBackend.Features.Cards;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface IGetArchivedBoardCardsHandler
    {
        Task<IActionResult> HandleAsync(
            string boardId,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class GetArchivedBoardCardsHandler : IGetArchivedBoardCardsHandler
    {
        private readonly AppDbContext _context;
        private readonly IIdHasher _idHasher;

        public GetArchivedBoardCardsHandler(AppDbContext context, IIdHasher idHasher)
        {
            _context = context;
            _idHasher = idHasher;
        }

        public async Task<IActionResult> HandleAsync(
            string boardId,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (!HashIdParser.TryDecode(_idHasher, boardId, out var decodedBoardId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var userId = user.GetCurrentUserId(_idHasher);

            var boardExists = await _context.Boards
                .AnyAsync(b =>
                    b.Id == decodedBoardId &&
                    (b.UserId == userId || b.Members.Any(m => m.UserId == userId && m.IsAccepted)),
                    cancellationToken);

            if (!boardExists)
            {
                return new NotFoundObjectResult("Board not found.");
            }

            var columns = await _context.Columns
                .Where(c => c.BoardId == decodedBoardId)
                .Select(c => new { c.Id, c.Title, c.ArchivedAt })
                .ToListAsync(cancellationToken);

            var columnDict = columns.ToDictionary(c => c.Id);

            var cards = await _context.Cards
                .Where(c => c.BoardId == decodedBoardId && c.ArchivedManually == true)
                .OrderByDescending(c => c.ArchivedAt)
                .ToListAsync(cancellationToken);

            var result = cards
                .Select(c =>
                {
                    string status;
                    string? colTitle = null;

                    if (!c.PreviousColumnId.HasValue)
                    {
                        status = "NoColumn";
                    }
                    else if (!columnDict.ContainsKey(c.PreviousColumnId.Value))
                    {
                        status = "Deleted";
                    }
                    else
                    {
                        var col = columnDict[c.PreviousColumnId.Value];
                        colTitle = col.Title;
                        status = col.ArchivedAt.HasValue ? "Archived" : "Active";
                    }

                    return new ArchivedCardDto
                    {
                        Id = _idHasher.Encode(c.Id),
                        Title = c.Title,
                        Order = c.Order,
                        ColumnId = c.ColumnId != null ? _idHasher.Encode(c.ColumnId.Value) : null,
                        PreviousColumnId = c.PreviousColumnId != null ? _idHasher.Encode(c.PreviousColumnId.Value) : null,
                        AssigneeId = c.AssigneeId != null ? _idHasher.Encode(c.AssigneeId.Value) : null,
                        ArchivedAt = c.ArchivedAt,
                        ArchivedManually = c.ArchivedManually,
                        ColumnStatus = status,
                        ColumnTitle = colTitle
                    };
                })
                .ToList();

            return new OkObjectResult(result);
        }
    }
}
