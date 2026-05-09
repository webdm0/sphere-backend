using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Events
{
    public interface ISubscribeBoardEventsHandler
    {
        Task HandleAsync(
            string boardId,
            ClaimsPrincipal user,
            HttpContext httpContext,
            CancellationToken cancellationToken = default);
    }

    public sealed class SubscribeBoardEventsHandler : ISubscribeBoardEventsHandler
    {
        private readonly IBoardEventService _eventService;
        private readonly AppDbContext _context;
        private readonly IIdHasher _idHasher;

        public SubscribeBoardEventsHandler(
            IBoardEventService eventService,
            AppDbContext context,
            IIdHasher idHasher)
        {
            _eventService = eventService;
            _context = context;
            _idHasher = idHasher;
        }

        public async Task HandleAsync(
            string boardId,
            ClaimsPrincipal user,
            HttpContext httpContext,
            CancellationToken cancellationToken = default)
        {
            if (!HashIdParser.TryDecode(_idHasher, boardId, out var decodedBoardId))
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var userId = user.GetCurrentUserId(_idHasher);

            var accessInfo = await _context.Boards
                .Where(b => b.Id == decodedBoardId)
                .Select(b => new
                {
                    IsOwner = b.UserId == userId,
                    b.CreatedAt,
                    MemberDateAdded = b.Members
                        .Where(m => m.UserId == userId && m.IsAccepted)
                        .Select(m => (DateTime?)m.DateAdded)
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (accessInfo == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var hasAccess = accessInfo.IsOwner || accessInfo.MemberDateAdded.HasValue;
            if (!hasAccess)
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var accessGrantedAtUtc = accessInfo.IsOwner
                ? accessInfo.CreatedAt
                : accessInfo.MemberDateAdded!.Value;

            await _eventService.SubscribeAsync(
                decodedBoardId,
                userId,
                accessGrantedAtUtc,
                httpContext,
                cancellationToken);
        }
    }
}
