using AutoMapper;
using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Models;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Columns
{
    public interface ICreateColumnHandler
    {
        Task<ActionResult<ColumnShortDto>> HandleAsync(
            CreateColumnDto dto,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class CreateColumnHandler : ICreateColumnHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;
        private readonly IMapper _mapper;

        public CreateColumnHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher, IMapper mapper)
        {
            _context = context;
            _eventService = eventService;
            _idHasher = idHasher;
            _mapper = mapper;
        }

        public async Task<ActionResult<ColumnShortDto>> HandleAsync(
            CreateColumnDto dto,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (!HashIdParser.TryDecode(_idHasher, dto.BoardId, out var boardId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var normalizedTitle = dto.Title.Trim();
            if (normalizedTitle.Length == 0)
            {
                return new BadRequestObjectResult("Title is required.");
            }

            var userId = user.GetCurrentUserId(_idHasher);

            try
            {
                var createdColumnId = 0;
                var result = await _context.ExecuteWithBoardOrderGuardAsync<ActionResult<ColumnShortDto>>(boardId, async _ =>
                {
                    var board = await _context.Boards
                        .Include(b => b.Columns)
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

                    var activeColumns = board.Columns
                        .Where(c => c.ArchivedAt == null)
                        .OrderBy(c => c.Order)
                        .ThenBy(c => c.Id)
                        .ToList();

                    ColumnFeatureHelpers.AssignSequentialOrders(activeColumns);

                    var column = new Column
                    {
                        Title = normalizedTitle,
                        BoardId = boardId,
                        Order = activeColumns.Count
                    };

                    _context.Columns.Add(column);
                    await _context.SaveChangesAsync(cancellationToken);

                    createdColumnId = column.Id;
                    var dtoResult = _mapper.Map<ColumnShortDto>(column);
                    return new CreatedResult($"/api/boards/{_idHasher.Encode(column.BoardId)}", dtoResult);
                }, cancellationToken);

                if (createdColumnId != 0)
                {
                    await _eventService.NotifyBoardAsync(boardId, "COLUMN_CREATED", new { columnId = _idHasher.Encode(createdColumnId) });
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
