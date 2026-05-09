using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Models;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface ICreateBoardHandler
    {
        Task<IActionResult> HandleAsync(
            CreateBoardDto dto,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class CreateBoardHandler : ICreateBoardHandler
    {
        private readonly AppDbContext _context;
        private readonly IIdHasher _idHasher;

        public CreateBoardHandler(AppDbContext context, IIdHasher idHasher)
        {
            _context = context;
            _idHasher = idHasher;
        }

        public async Task<IActionResult> HandleAsync(
            CreateBoardDto dto,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            var userId = user.GetCurrentUserId(_idHasher);

            if (!HashIdParser.TryDecodeMany(_idHasher, dto.UserIds, out var parsedUserIds))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var normalizedTitle = dto.Title.Trim();
            if (normalizedTitle.Length == 0)
            {
                return new BadRequestObjectResult("Title is required.");
            }

            var invitedUserIds = parsedUserIds
                .Where(uid => uid != userId)
                .Distinct()
                .ToList();

            if (invitedUserIds.Count > 0)
            {
                var invitedUsersCount = await _context.Users
                    .Where(u => invitedUserIds.Contains(u.Id) && u.IsEmailConfirmed && !u.IsDemo)
                    .CountAsync(cancellationToken);

                if (invitedUsersCount != invitedUserIds.Count)
                {
                    return new BadRequestObjectResult("Some selected users can't be invited.");
                }
            }

            try
            {
                var allUserIds = invitedUserIds
                    .Prepend(userId)
                    .Distinct()
                    .ToList();

                var result = await _context.ExecuteWithUserBoardOrderGuardAsync<IActionResult>(allUserIds, async _ =>
                {
                    var existingMembers = await _context.BoardMembers
                        .Where(m => allUserIds.Contains(m.UserId))
                        .OrderBy(m => m.UserId)
                        .ThenBy(m => m.Order)
                        .ThenBy(m => m.BoardId)
                        .ToListAsync(cancellationToken);

                    var nextOrders = new Dictionary<int, int>();
                    foreach (var group in existingMembers.GroupBy(m => m.UserId))
                    {
                        var orderedGroup = group
                            .OrderBy(m => m.Order)
                            .ThenBy(m => m.BoardId)
                            .ToList();

                        BoardFeatureHelpers.AssignSequentialBoardMemberOrders(orderedGroup);
                        nextOrders[group.Key] = orderedGroup.Count;
                    }

                    var board = new Board
                    {
                        Title = normalizedTitle,
                        UserId = userId
                    };
                    _context.Boards.Add(board);
                    await _context.SaveChangesAsync(cancellationToken);

                    _context.Columns.AddRange(
                        new Column { Title = "Queue", BoardId = board.Id, Order = 0 },
                        new Column { Title = "Active", BoardId = board.Id, Order = 1 },
                        new Column { Title = "Done", BoardId = board.Id, Order = 2 }
                    );

                    var now = DateTime.UtcNow;
                    var members = allUserIds.Select(uid => new BoardMember
                    {
                        BoardId = board.Id,
                        UserId = uid,
                        IsAccepted = uid == userId,
                        DateAdded = now,
                        Order = nextOrders.TryGetValue(uid, out var nextOrder) ? nextOrder : 0
                    }).ToList();

                    _context.BoardMembers.AddRange(members);

                    await _context.SaveChangesAsync(cancellationToken);

                    return new OkObjectResult(new { Id = _idHasher.Encode(board.Id), board.Title });
                }, cancellationToken);

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
                return new ObjectResult("Unable to create the board right now.")
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }
    }
}
