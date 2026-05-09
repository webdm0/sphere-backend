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
    public interface IGetBoardColumnsHandler
    {
        Task<IActionResult> HandleAsync(
            string boardId,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class GetBoardColumnsHandler : IGetBoardColumnsHandler
    {
        private readonly AppDbContext _context;
        private readonly IIdHasher _idHasher;

        public GetBoardColumnsHandler(AppDbContext context, IIdHasher idHasher)
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
                .Where(c => c.BoardId == decodedBoardId && c.ArchivedAt == null)
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Id)
                .Select(c => new
                {
                    c.Id,
                    Title = c.Title,
                    c.Order,
                    ArchivedAt = c.ArchivedAt,
                    Cards = c.Cards
                        .Where(card => card.ArchivedAt == null && card.ColumnId != null)
                        .OrderBy(card => card.Order)
                        .ThenBy(card => card.Id)
                        .Select(card => new
                        {
                            card.Id,
                            Title = card.Title,
                            card.Order,
                            card.ColumnId,
                            card.AssigneeId,
                            ArchivedAt = card.ArchivedAt
                        })
                        .ToList()
                })
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var response = columns.Select(c => new ColumnShortDto
            {
                Id = _idHasher.Encode(c.Id),
                Title = c.Title,
                Order = c.Order,
                ArchivedAt = c.ArchivedAt,
                Cards = c.Cards.Select(card => new CardShortDto
                {
                    Id = _idHasher.Encode(card.Id),
                    Title = card.Title,
                    Order = card.Order,
                    ColumnId = card.ColumnId.HasValue ? _idHasher.Encode(card.ColumnId.Value) : null,
                    AssigneeId = card.AssigneeId.HasValue ? _idHasher.Encode(card.AssigneeId.Value) : null,
                    ArchivedAt = card.ArchivedAt
                }).ToList()
            }).ToList();

            return new OkObjectResult(response);
        }
    }
}
