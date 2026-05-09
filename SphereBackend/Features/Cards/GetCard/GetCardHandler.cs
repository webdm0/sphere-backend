using AutoMapper;
using SphereBackend.Data;
using SphereBackend.Extensions;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SphereBackend.Features.Cards
{
    public interface IGetCardHandler
    {
        Task<IActionResult> HandleAsync(
            string id,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default);
    }

    public sealed class GetCardHandler : IGetCardHandler
    {
        private readonly AppDbContext _context;
        private readonly IIdHasher _idHasher;
        private readonly IMapper _mapper;

        public GetCardHandler(AppDbContext context, IIdHasher idHasher, IMapper mapper)
        {
            _context = context;
            _idHasher = idHasher;
            _mapper = mapper;
        }

        public async Task<IActionResult> HandleAsync(
            string id,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (!HashIdParser.TryDecode(_idHasher, id, out var cardId))
            {
                return new BadRequestObjectResult("Invalid request.");
            }

            var userId = user.GetCurrentUserId(_idHasher);

            var card = await _context.Cards
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.Id == cardId &&
                    (c.Board.UserId == userId ||
                     c.Board.Members.Any(m => m.UserId == userId && m.IsAccepted)),
                    cancellationToken);

            if (card == null)
            {
                return new NotFoundObjectResult("Card not found.");
            }

            return new OkObjectResult(CardFeatureHelpers.MapToDto(_mapper, card));
        }
    }
}
