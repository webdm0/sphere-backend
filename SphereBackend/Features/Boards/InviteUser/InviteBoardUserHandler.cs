using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Models;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface IInviteBoardUserHandler
    {
        Task<IActionResult> HandleAsync(
            string boardId,
            string username,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class InviteBoardUserHandler : IInviteBoardUserHandler
    {
        private readonly AppDbContext _context;
        private readonly IBoardEventService _eventService;
        private readonly IIdHasher _idHasher;

        public InviteBoardUserHandler(AppDbContext context, IBoardEventService eventService, IIdHasher idHasher)
        {
            _context = context;
            _eventService = eventService;
            _idHasher = idHasher;
        }

        public async Task<IActionResult> HandleAsync(
            string boardId,
            string username,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (!HashIdParser.TryDecode(_idHasher, boardId, out var decodedBoardId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var userId = user.GetCurrentUserId(_idHasher);

            if (string.IsNullOrWhiteSpace(username))
            {
                return new BadRequestObjectResult("Enter a username.");
            }

            username = username.Trim();
            var normalizedUsername = username.ToLowerInvariant();

            var board = await _context.Boards
                .FirstOrDefaultAsync(b => b.Id == decodedBoardId && b.UserId == userId, cancellationToken);

            if (board == null)
            {
                return new NotFoundObjectResult("Board not found.");
            }

            if (board.ArchivedAt != null)
            {
                return new BadRequestObjectResult("Archived boards can't be changed.");
            }

            var inviteProcessedResponse = new OkObjectResult(new { message = "If the request is valid, the invitation has been processed." });

            if (!BoardFeatureHelpers.IsAsciiUsername(username))
            {
                return inviteProcessedResponse;
            }

            var invitedUser = await _context.Users
                .FirstOrDefaultAsync(u => u.IsEmailConfirmed && !u.IsDemo && u.NormalizedUsername == normalizedUsername, cancellationToken);
            if (invitedUser == null)
            {
                return inviteProcessedResponse;
            }

            try
            {
                var invited = false;
                var result = await _context.ExecuteWithUserBoardOrderGuardAsync<IActionResult>(invitedUser.Id, async _ =>
                {
                    var exists = await _context.BoardMembers
                        .AnyAsync(bm => bm.BoardId == decodedBoardId && bm.UserId == invitedUser.Id, cancellationToken);

                    if (exists)
                    {
                        return inviteProcessedResponse;
                    }

                    var memberships = await _context.BoardMembers
                        .Where(m => m.UserId == invitedUser.Id)
                        .OrderBy(m => m.Order)
                        .ThenBy(m => m.BoardId)
                        .ToListAsync(cancellationToken);

                    BoardFeatureHelpers.AssignSequentialBoardMemberOrders(memberships);

                    var member = new BoardMember
                    {
                        BoardId = decodedBoardId,
                        UserId = invitedUser.Id,
                        IsAccepted = false,
                        DateAdded = DateTime.UtcNow,
                        Order = memberships.Count
                    };

                    _context.BoardMembers.Add(member);
                    await _context.SaveChangesAsync(cancellationToken);
                    invited = true;
                    return inviteProcessedResponse;
                }, cancellationToken);

                if (invited)
                {
                    await _eventService.NotifyBoardAsync(decodedBoardId, "MEMBER_INVITED");
                }

                return result;
            }
            catch (DbUpdateException ex) when (ex.IsOrderUniquenessViolation())
            {
                return inviteProcessedResponse;
            }
            catch (DbUpdateException)
            {
                return inviteProcessedResponse;
            }
        }
    }
}
