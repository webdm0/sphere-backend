using SphereBackend.Data;
using SphereBackend.Features.Cards;
using SphereBackend.Features.Columns;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface IGetArchivedBoardColumnsHandler
    {
        Task<IActionResult> HandleAsync(
            string boardId,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class GetArchivedBoardColumnsHandler : IGetArchivedBoardColumnsHandler
    {
        private readonly AppDbContext _context;
        private readonly IIdHasher _idHasher;

        public GetArchivedBoardColumnsHandler(AppDbContext context, IIdHasher idHasher)
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
                .AnyAsync(b => b.Id == decodedBoardId &&
                    (b.UserId == userId || b.Members.Any(m => m.UserId == userId && m.IsAccepted)),
                    cancellationToken);

            if (!boardExists)
            {
                return new NotFoundObjectResult("Board not found.");
            }

            var archivedColumns = await _context.Columns
                .AsNoTracking()
                .Where(c => c.BoardId == decodedBoardId && c.ArchivedAt != null)
                .OrderByDescending(c => c.ArchivedAt)
                .ToListAsync(cancellationToken);

            if (archivedColumns.Count == 0)
            {
                return new OkObjectResult(new List<ArchivedColumnShortDto>());
            }

            var colIds = archivedColumns.Select(c => c.Id).ToList();

            var archivedCardsFromColumns = await _context.Cards
                .AsNoTracking()
                .Where(c => c.PreviousColumnId != null &&
                            colIds.Contains(c.PreviousColumnId.Value) &&
                            c.ArchivedManually == false)
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Id)
                .Select(c => new
                {
                    c.Id,
                    Title = c.Title,
                    c.Order,
                    c.ColumnId,
                    c.PreviousColumnId,
                    c.AssigneeId,
                    ArchivedAt = c.ArchivedAt,
                    ArchivedManually = c.ArchivedManually
                })
                .ToListAsync(cancellationToken);

            var cardsByColumn = archivedCardsFromColumns
                .GroupBy(c => c.PreviousColumnId)
                .Where(g => g.Key != null)
                .ToDictionary(g => g.Key!.Value, g => g.Select(card => new ArchivedCardDto
                {
                    Id = _idHasher.Encode(card.Id),
                    Title = card.Title,
                    Order = card.Order,
                    ColumnId = card.ColumnId.HasValue ? _idHasher.Encode(card.ColumnId.Value) : null,
                    PreviousColumnId = card.PreviousColumnId.HasValue ? _idHasher.Encode(card.PreviousColumnId.Value) : null,
                    AssigneeId = card.AssigneeId.HasValue ? _idHasher.Encode(card.AssigneeId.Value) : null,
                    ArchivedAt = card.ArchivedAt,
                    ArchivedManually = card.ArchivedManually
                }).ToList());

            var response = archivedColumns.Select(c => new ArchivedColumnShortDto
            {
                Id = _idHasher.Encode(c.Id),
                Title = c.Title,
                Order = c.Order,
                ArchivedAt = c.ArchivedAt,
                Cards = cardsByColumn.ContainsKey(c.Id)
                    ? cardsByColumn[c.Id]
                    : new List<ArchivedCardDto>()
            }).ToList();

            return new OkObjectResult(response);
        }
    }
}
