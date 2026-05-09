using AutoMapper;
using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Cards
{
    public interface IUpdateCardHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            UpdateCardDto dto,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class UpdateCardHandler : IUpdateCardHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;
        private readonly IMapper _mapper;

        public UpdateCardHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher, IMapper mapper)
        {
            _context = context;
            _eventService = eventService;
            _idHasher = idHasher;
            _mapper = mapper;
        }

        public async Task<IActionResult> HandleAsync(
            string id,
            UpdateCardDto dto,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (!HashIdParser.TryDecode(_idHasher, id, out var cardId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var userId = user.GetCurrentUserId(_idHasher);

            if (!dto.IsArchivedSpecified || dto.IsArchived != true)
            {
                var card = await _context.Cards
                    .Include(c => c.Board)
                    .FirstOrDefaultAsync(c =>
                        c.Id == cardId &&
                        c.ArchivedAt == null &&
                        (c.Board.UserId == userId ||
                         c.Board.Members.Any(m => m.UserId == userId && m.IsAccepted)),
                        cancellationToken);

                if (card == null)
                {
                    return new NotFoundObjectResult("Card not found.");
                }

                if (card.Board.ArchivedAt != null)
                {
                    return new BadRequestObjectResult("Archived boards can't be changed.");
                }

                var assigneeValidation = await ApplyCardUpdatesAsync(card, dto, userId, cancellationToken);
                if (assigneeValidation != null)
                {
                    return assigneeValidation;
                }

                if (dto.IsArchivedSpecified && dto.IsArchived != true)
                {
                    return new BadRequestObjectResult("Use restore to unarchive this card.");
                }

                card.UpdatedById = userId;
                card.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);

                await _eventService.NotifyBoardAsync(card.BoardId, "CARD_UPDATED", new { cardId = _idHasher.Encode(card.Id) });

                return new OkObjectResult(CardFeatureHelpers.MapToDto(_mapper, card));
            }

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
                var updatedCardId = 0;
                var result = await _context.ExecuteWithBoardOrderGuardAsync<IActionResult>(boardId.Value, async _ =>
                {
                    var card = await _context.Cards
                        .Include(c => c.Board)
                        .FirstOrDefaultAsync(c =>
                            c.Id == cardId &&
                            c.ArchivedAt == null &&
                            (c.Board.UserId == userId ||
                             c.Board.Members.Any(m => m.UserId == userId && m.IsAccepted)),
                            cancellationToken);

                    if (card == null)
                    {
                        return new NotFoundObjectResult("Card not found.");
                    }

                    if (card.Board.ArchivedAt != null)
                    {
                        return new BadRequestObjectResult("Archived boards can't be changed.");
                    }

                    var assigneeValidation = await ApplyCardUpdatesAsync(card, dto, userId, cancellationToken);
                    if (assigneeValidation != null)
                    {
                        return assigneeValidation;
                    }

                    var remainingSourceCards = card.ColumnId.HasValue
                        ? await _context.Cards
                            .Where(c => c.ColumnId == card.ColumnId.Value &&
                                        c.ArchivedAt == null &&
                                        c.Id != card.Id)
                            .OrderBy(c => c.Order)
                            .ThenBy(c => c.Id)
                            .ToListAsync(cancellationToken)
                        : new List<Models.Card>();

                    var now = DateTime.UtcNow;
                    card.PreviousColumnId = card.ColumnId;
                    card.ColumnId = null;
                    card.ArchivedAt = now;
                    card.ArchivedManually = true;

                    CardFeatureHelpers.AssignSequentialOrders(remainingSourceCards, now, userId);

                    card.UpdatedById = userId;
                    card.UpdatedAt = now;

                    await _context.SaveChangesAsync(cancellationToken);

                    updatedCardId = card.Id;
                    return new OkObjectResult(CardFeatureHelpers.MapToDto(_mapper, card));
                }, cancellationToken);

                if (updatedCardId != 0)
                {
                    await _eventService.NotifyBoardAsync(boardId.Value, "CARD_UPDATED", new { cardId = _idHasher.Encode(updatedCardId) });
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

        private async Task<IActionResult?> ApplyCardUpdatesAsync(
            Models.Card card,
            UpdateCardDto dto,
            int userId,
            CancellationToken cancellationToken)
        {
            if (dto.TitleSpecified)
            {
                if (string.IsNullOrWhiteSpace(dto.Title))
                {
                    return new BadRequestObjectResult("Title is required.");
                }

                card.Title = dto.Title.Trim();
            }

            if (dto.ContentSpecified)
            {
                var content = dto.Content ?? string.Empty;
                if (content.Length > CardValidationRules.MaxContentLength)
                {
                    return new BadRequestObjectResult(CardValidationRules.ContentTooLongMessage);
                }

                card.Content = content;
            }

            if (dto.PrioritySpecified)
            {
                if (string.IsNullOrWhiteSpace(dto.Priority))
                {
                    card.Priority = null;
                }
                else
                {
                    var normalizedPriority = dto.Priority.Trim();
                    if (!CardValidationRules.IsAllowedPriority(normalizedPriority))
                    {
                        return new BadRequestObjectResult(CardValidationRules.InvalidPriorityMessage);
                    }

                    card.Priority = normalizedPriority;
                }
            }

            if (dto.StartAtSpecified) card.StartAt = dto.StartAt;
            if (dto.DueAtSpecified) card.DueAt = dto.DueAt;

            if (dto.AssigneeIdSpecified)
            {
                if (!HashIdParser.TryDecodeNullable(_idHasher, dto.AssigneeId, out var assigneeId))
                {
                    return new BadRequestObjectResult("Invalid request.");
                }

                if (assigneeId != null)
                {
                    var isBoardOwner = card.Board.UserId == assigneeId.Value;
                    var isAcceptedMember = await _context.BoardMembers
                        .AnyAsync(m =>
                            m.BoardId == card.BoardId &&
                            m.UserId == assigneeId.Value &&
                            m.IsAccepted,
                            cancellationToken);

                    if (!isBoardOwner && !isAcceptedMember)
                    {
                        return new BadRequestObjectResult("Assignee must be a board member.");
                    }
                }

                card.AssigneeId = assigneeId;

                if (assigneeId is null)
                {
                    card.Assignee = null;
                }

                _context.Entry(card)
                    .Property(c => c.AssigneeId)
                    .IsModified = true;
            }

            return null;
        }
    }
}
