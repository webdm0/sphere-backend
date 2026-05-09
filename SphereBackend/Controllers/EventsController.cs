using SphereBackend.Features.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SphereBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class EventsController : ControllerBase
    {
        private readonly ISubscribeBoardEventsHandler _subscribeBoardEventsHandler;

        public EventsController(ISubscribeBoardEventsHandler subscribeBoardEventsHandler)
        {
            _subscribeBoardEventsHandler = subscribeBoardEventsHandler;
        }

        [HttpGet("{boardId}")]
        public async Task Subscribe(string boardId, CancellationToken ct)
        {
            await _subscribeBoardEventsHandler.HandleAsync(boardId, User, HttpContext, ct);
        }
    }
}

