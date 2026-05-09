using SphereBackend.Features.Cards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SphereBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CardsController : ControllerBase
    {
        private readonly ICreateCardHandler _createCardHandler;
        private readonly IUpdateCardHandler _updateCardHandler;
        private readonly IRestoreCardHandler _restoreCardHandler;
        private readonly IDeleteCardForeverHandler _deleteCardForeverHandler;
        private readonly IGetCardHandler _getCardHandler;
        private readonly IReorderCardsHandler _reorderCardsHandler;

        public CardsController(
            ICreateCardHandler createCardHandler,
            IUpdateCardHandler updateCardHandler,
            IRestoreCardHandler restoreCardHandler,
            IDeleteCardForeverHandler deleteCardForeverHandler,
            IGetCardHandler getCardHandler,
            IReorderCardsHandler reorderCardsHandler)
        {
            _createCardHandler = createCardHandler;
            _updateCardHandler = updateCardHandler;
            _restoreCardHandler = restoreCardHandler;
            _deleteCardForeverHandler = deleteCardForeverHandler;
            _getCardHandler = getCardHandler;
            _reorderCardsHandler = reorderCardsHandler;
        }

        [HttpPost]
        public Task<IActionResult> CreateCard([FromBody] CreateCardDto dto, CancellationToken cancellationToken)
        {
            return _createCardHandler.HandleAsync(dto, User, cancellationToken);
        }

        [HttpPatch("{id}")]
        public Task<IActionResult> UpdateCard(string id, [FromBody] UpdateCardDto dto, CancellationToken cancellationToken)
        {
            return _updateCardHandler.HandleAsync(id, dto, User, cancellationToken);
        }

        [HttpPost("{id}/restore")]
        public Task<IActionResult> RestoreCard(string id, CancellationToken cancellationToken)
        {
            return _restoreCardHandler.HandleAsync(id, User, cancellationToken);
        }

        [HttpDelete("{id}/permanent")]
        public Task<IActionResult> DeleteCardForever(string id, CancellationToken cancellationToken)
        {
            return _deleteCardForeverHandler.HandleAsync(id, User, cancellationToken);
        }

        [HttpGet("{id}")]
        public Task<IActionResult> GetCard(string id, CancellationToken cancellationToken)
        {
            return _getCardHandler.HandleAsync(id, User, cancellationToken);
        }

        [HttpPut("reorder")]
        public Task<IActionResult> ReorderCards([FromBody] ReorderCardsRequestDto request, CancellationToken cancellationToken)
        {
            return _reorderCardsHandler.HandleAsync(request, User, cancellationToken);
        }
    }
}
