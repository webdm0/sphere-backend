using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Boards
{
    public interface IGetBoardMembersHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class GetBoardMembersHandler : IGetBoardMembersHandler
    {
        private readonly AppDbContext _context;
        private readonly IIdHasher _idHasher;

        public GetBoardMembersHandler(AppDbContext context, IIdHasher idHasher)
        {
            _context = context;
            _idHasher = idHasher;
        }

        public async Task<IActionResult> HandleAsync(
            string id,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (!HashIdParser.TryDecode(_idHasher, id, out var boardId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var userId = user.GetCurrentUserId(_idHasher);

            var boardAccess = await _context.Boards
                .AsNoTracking()
                .Where(b =>
                    b.Id == boardId &&
                    (b.UserId == userId || b.Members.Any(m => m.UserId == userId && m.IsAccepted)))
                .Select(b => new
                {
                    b.UserId,
                    OwnerUsername = b.User.Username,
                    OwnerEmail = b.User.Email
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (boardAccess == null)
            {
                return new NotFoundObjectResult("Board not found.");
            }

            var canViewAllEmails = boardAccess.UserId == userId;

            var boardMembers = await _context.BoardMembers
                .AsNoTracking()
                .Where(m => m.BoardId == boardId)
                .OrderBy(m => m.DateAdded)
                .ThenBy(m => m.UserId)
                .Select(m => new
                {
                    m.UserId,
                    Username = m.User.Username,
                    Email = (canViewAllEmails || m.UserId == userId) ? m.User.Email : null,
                    m.IsAccepted
                })
                .ToListAsync(cancellationToken);

            var members = new List<BoardMemberResponse>
            {
                new()
                {
                    UserId = _idHasher.Encode(boardAccess.UserId),
                    Username = boardAccess.OwnerUsername,
                    Email = (canViewAllEmails || boardAccess.UserId == userId) ? boardAccess.OwnerEmail : null,
                    IsAccepted = true
                }
            };

            members.AddRange(boardMembers
                .Where(m => m.UserId != boardAccess.UserId)
                .Select(m => new BoardMemberResponse
                {
                    UserId = _idHasher.Encode(m.UserId),
                    Username = m.Username,
                    Email = m.Email,
                    IsAccepted = m.IsAccepted
                }));

            return new OkObjectResult(new
            {
                ownerId = _idHasher.Encode(boardAccess.UserId),
                members
            });
        }

        private sealed class BoardMemberResponse
        {
            public string UserId { get; init; } = string.Empty;
            public string Username { get; init; } = string.Empty;
            public string? Email { get; init; }
            public bool IsAccepted { get; init; }
        }
    }
}
