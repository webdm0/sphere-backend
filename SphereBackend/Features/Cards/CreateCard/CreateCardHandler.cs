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
    public interface ICreateCardHandler
    {
        Task<IActionResult> HandleAsync(
            CreateCardDto dto,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class CreateCardHandler : ICreateCardHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;
        private readonly IMapper _mapper;

        public CreateCardHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher, IMapper mapper)
        {
            _context = context;
            _eventService = eventService;
            _idHasher = idHasher;
            _mapper = mapper;
        }

        public async Task<IActionResult> HandleAsync(
            CreateCardDto dto,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (!HashIdParser.TryDecode(_idHasher, dto.ColumnId, out var columnId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var userId = user.GetCurrentUserId(_idHasher);
            var normalizedTitle = dto.Title.Trim();
            if (normalizedTitle.Length == 0)
            {
                return new BadRequestObjectResult("Title is required.");
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
                var createdCardId = 0;
                var result = await _context.ExecuteWithBoardOrderGuardAsync<IActionResult>(boardId.Value, async _ =>
                {
                    var column = await _context.Columns
                        .Include(c => c.Board)
                        .Include(c => c.Cards)
                        .FirstOrDefaultAsync(c =>
                            c.Id == columnId &&
                            c.ArchivedAt == null &&
                            (c.Board.UserId == userId ||
                             c.Board.Members.Any(m => m.UserId == userId && m.IsAccepted)),
                            cancellationToken);

                    if (column == null)
                    {
                        return new NotFoundObjectResult("Column not found.");
                    }

                    if (column.Board.ArchivedAt != null)
                    {
                        return new BadRequestObjectResult("Archived boards can't be changed.");
                    }

                    var now = DateTime.UtcNow;
                    var activeCards = column.Cards
                        .Where(c => c.ArchivedAt == null && c.ColumnId == columnId)
                        .OrderBy(c => c.Order)
                        .ThenBy(c => c.Id)
                        .ToList();

                    CardFeatureHelpers.AssignSequentialOrders(activeCards, now, userId);

                    var card = new Card
                    {
                        Title = normalizedTitle,
                        Content = dto.Content?.Trim() ?? string.Empty,
                        ColumnId = columnId,
                        BoardId = column.BoardId,
                        Order = activeCards.Count,
                        AssigneeId = dto.AssignToMe ? userId : null,
                        CreatedById = userId,
                        UpdatedById = userId,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    _context.Cards.Add(card);
                    await _context.SaveChangesAsync(cancellationToken);

                    createdCardId = card.Id;
                    return new OkObjectResult(CardFeatureHelpers.MapToDto(_mapper, card));
                }, cancellationToken);

                if (createdCardId != 0)
                {
                    await _eventService.NotifyBoardAsync(boardId.Value, "CARD_CREATED", new { cardId = _idHasher.Encode(createdCardId) });
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
                return new ObjectResult("Unable to create the card right now.")
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }
    }
}
