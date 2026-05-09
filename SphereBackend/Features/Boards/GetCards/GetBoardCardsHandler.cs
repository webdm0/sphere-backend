using SphereBackend.Data;
using SphereBackend.Features.Cards;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface IGetBoardCardsHandler
    {
        Task<IActionResult> HandleAsync(
            string boardId,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class GetBoardCardsHandler : IGetBoardCardsHandler
    {
        private readonly AppDbContext _context;
        private readonly IIdHasher _idHasher;

        public GetBoardCardsHandler(AppDbContext context, IIdHasher idHasher)
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

            var cards = await _context.Cards
                .Where(c =>
                    c.ArchivedAt == null && c.ColumnId != null && c.BoardId == decodedBoardId)
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Id)
                .Select(c => new
                {
                    c.Id,
                    Title = c.Title,
                    c.Order,
                    c.ColumnId,
                    c.AssigneeId,
                    ArchivedAt = c.ArchivedAt
                })
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var response = cards.Select(c => new CardShortDto
            {
                Id = _idHasher.Encode(c.Id),
                Title = c.Title,
                Order = c.Order,
                ColumnId = c.ColumnId.HasValue ? _idHasher.Encode(c.ColumnId.Value) : null,
                AssigneeId = c.AssigneeId.HasValue ? _idHasher.Encode(c.AssigneeId.Value) : null,
                ArchivedAt = c.ArchivedAt
            }).ToList();

            return new OkObjectResult(response);
        }
    }
}
