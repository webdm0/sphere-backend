using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Models;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface IAddBoardMemberHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            AddMemberDto dto,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class AddBoardMemberHandler : IAddBoardMemberHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;

        public AddBoardMemberHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher)
        {
            _context = context;
            _eventService = eventService;
            _idHasher = idHasher;
        }

        public async Task<IActionResult> HandleAsync(
            string id,
            AddMemberDto dto,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (!HashIdParser.TryDecode(_idHasher, id, out var boardId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var userId = user.GetCurrentUserId(_idHasher);

            if (!HashIdParser.TryDecode(_idHasher, dto.UserId, out var memberUserId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var board = await _context.Boards
                .FirstOrDefaultAsync(b => b.Id == boardId && b.UserId == userId, cancellationToken);

            if (board == null)
            {
                return new NotFoundObjectResult("Board not found.");
            }

            if (board.ArchivedAt != null)
            {
                return new BadRequestObjectResult("Archived boards can't be changed.");
            }

            var invitedUserExists = await _context.Users
                .AnyAsync(u => u.Id == memberUserId && u.IsEmailConfirmed && !u.IsDemo, cancellationToken);
            if (!invitedUserExists)
            {
                return new BadRequestObjectResult("This user can't be invited.");
            }

            var alreadyMember = await _context.BoardMembers
                .AnyAsync(m => m.BoardId == boardId && m.UserId == memberUserId, cancellationToken);
            if (alreadyMember)
            {
                return new BadRequestObjectResult("This user is already a board member.");
            }

            try
            {
                var memberAdded = false;
                var result = await _context.ExecuteWithUserBoardOrderGuardAsync<IActionResult>(memberUserId, async _ =>
                {
                    var existingMembership = await _context.BoardMembers
                        .AnyAsync(m => m.BoardId == boardId && m.UserId == memberUserId, cancellationToken);

                    if (existingMembership)
                    {
                        return new BadRequestObjectResult("This user is already a board member.");
                    }

                    var memberships = await _context.BoardMembers
                        .Where(m => m.UserId == memberUserId)
                        .OrderBy(m => m.Order)
                        .ThenBy(m => m.BoardId)
                        .ToListAsync(cancellationToken);

                    BoardFeatureHelpers.AssignSequentialBoardMemberOrders(memberships);

                    var member = new BoardMember
                    {
                        BoardId = boardId,
                        UserId = memberUserId,
                        IsAccepted = false,
                        DateAdded = DateTime.UtcNow,
                        Order = memberships.Count
                    };

                    _context.BoardMembers.Add(member);
                    await _context.SaveChangesAsync(cancellationToken);
                    memberAdded = true;
                    return new OkResult();
                }, cancellationToken);

                if (memberAdded)
                {
                    await _eventService.NotifyBoardAsync(boardId, "MEMBER_INVITED");
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
            catch (DbUpdateException)
            {
                return new BadRequestObjectResult("This user can't be invited.");
            }
        }
    }
}
