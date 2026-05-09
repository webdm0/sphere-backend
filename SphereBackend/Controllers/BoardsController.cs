using SphereBackend.Features.Auth;
using SphereBackend.Features.Boards;
using SphereBackend.Extensions;
using SphereBackend.Infrastructure.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SphereBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class BoardsController : ControllerBase
    {
        private readonly IGetBoardHandler _getBoardHandler;
        private readonly IGetBoardColumnsHandler _getBoardColumnsHandler;
        private readonly IGetArchivedBoardColumnsHandler _getArchivedBoardColumnsHandler;
        private readonly IGetBoardCardsHandler _getBoardCardsHandler;
        private readonly IGetArchivedBoardCardsHandler _getArchivedBoardCardsHandler;
        private readonly ICreateBoardHandler _createBoardHandler;
        private readonly IGetBoardsHandler _getBoardsHandler;
        private readonly IGetArchivedBoardsHandler _getArchivedBoardsHandler;
        private readonly ILeaveBoardHandler _leaveBoardHandler;
        private readonly IUpdateBoardHandler _updateBoardHandler;
        private readonly IArchiveBoardHandler _archiveBoardHandler;
        private readonly IRestoreBoardHandler _restoreBoardHandler;
        private readonly IDeleteBoardHandler _deleteBoardHandler;
        private readonly IInviteBoardUserHandler _inviteBoardUserHandler;
        private readonly IAcceptBoardInviteHandler _acceptBoardInviteHandler;
        private readonly IDeclineBoardInviteHandler _declineBoardInviteHandler;
        private readonly IGetBoardMembersHandler _getBoardMembersHandler;
        private readonly IAddBoardMemberHandler _addBoardMemberHandler;
        private readonly IRemoveBoardMemberHandler _removeBoardMemberHandler;

        public BoardsController(
            IGetBoardHandler getBoardHandler,
            IGetBoardColumnsHandler getBoardColumnsHandler,
            IGetArchivedBoardColumnsHandler getArchivedBoardColumnsHandler,
            IGetBoardCardsHandler getBoardCardsHandler,
            IGetArchivedBoardCardsHandler getArchivedBoardCardsHandler,
            ICreateBoardHandler createBoardHandler,
            IGetBoardsHandler getBoardsHandler,
            IGetArchivedBoardsHandler getArchivedBoardsHandler,
            ILeaveBoardHandler leaveBoardHandler,
            IUpdateBoardHandler updateBoardHandler,
            IArchiveBoardHandler archiveBoardHandler,
            IRestoreBoardHandler restoreBoardHandler,
            IDeleteBoardHandler deleteBoardHandler,
            IInviteBoardUserHandler inviteBoardUserHandler,
            IAcceptBoardInviteHandler acceptBoardInviteHandler,
            IDeclineBoardInviteHandler declineBoardInviteHandler,
            IGetBoardMembersHandler getBoardMembersHandler,
            IAddBoardMemberHandler addBoardMemberHandler,
            IRemoveBoardMemberHandler removeBoardMemberHandler)
        {
            _getBoardHandler = getBoardHandler;
            _getBoardColumnsHandler = getBoardColumnsHandler;
            _getArchivedBoardColumnsHandler = getArchivedBoardColumnsHandler;
            _getBoardCardsHandler = getBoardCardsHandler;
            _getArchivedBoardCardsHandler = getArchivedBoardCardsHandler;
            _createBoardHandler = createBoardHandler;
            _getBoardsHandler = getBoardsHandler;
            _getArchivedBoardsHandler = getArchivedBoardsHandler;
            _leaveBoardHandler = leaveBoardHandler;
            _updateBoardHandler = updateBoardHandler;
            _archiveBoardHandler = archiveBoardHandler;
            _restoreBoardHandler = restoreBoardHandler;
            _deleteBoardHandler = deleteBoardHandler;
            _inviteBoardUserHandler = inviteBoardUserHandler;
            _acceptBoardInviteHandler = acceptBoardInviteHandler;
            _declineBoardInviteHandler = declineBoardInviteHandler;
            _getBoardMembersHandler = getBoardMembersHandler;
            _addBoardMemberHandler = addBoardMemberHandler;
            _removeBoardMemberHandler = removeBoardMemberHandler;
        }

        [HttpGet("{id}")]
        public Task<IActionResult> GetBoard(string id, CancellationToken cancellationToken)
        {
            return _getBoardHandler.HandleAsync(id, User, cancellationToken);
        }

        [HttpGet("{boardId}/columns")]
        public Task<IActionResult> GetColumns(string boardId, CancellationToken cancellationToken)
        {
            return _getBoardColumnsHandler.HandleAsync(boardId, User, cancellationToken);
        }

        [HttpGet("{boardId}/columns/archived")]
        public Task<IActionResult> GetArchivedColumns(string boardId, CancellationToken cancellationToken)
        {
            return _getArchivedBoardColumnsHandler.HandleAsync(boardId, User, cancellationToken);
        }

        [HttpGet("{boardId}/cards")]
        public Task<IActionResult> GetCards(string boardId, CancellationToken cancellationToken)
        {
            return _getBoardCardsHandler.HandleAsync(boardId, User, cancellationToken);
        }

        [HttpGet("{boardId}/cards/archived")]
        public Task<IActionResult> GetArchivedCards(string boardId, CancellationToken cancellationToken)
        {
            return _getArchivedBoardCardsHandler.HandleAsync(boardId, User, cancellationToken);
        }

        [HttpPost]
        public Task<IActionResult> CreateBoard([FromBody] CreateBoardDto dto, CancellationToken cancellationToken)
        {
            if (User.IsDemoUser() && dto.UserIds.Count > 0)
            {
                return Task.FromResult(DemoFeatureForbidden());
            }

            return _createBoardHandler.HandleAsync(dto, User, cancellationToken);
        }

        [HttpGet]
        public Task<IActionResult> GetBoards(CancellationToken cancellationToken)
        {
            return _getBoardsHandler.HandleAsync(User, cancellationToken);
        }

        [HttpGet("archived")]
        public Task<IActionResult> GetArchivedBoards(CancellationToken cancellationToken)
        {
            return _getArchivedBoardsHandler.HandleAsync(User, cancellationToken);
        }

        [HttpDelete("{id}/leave")]
        public Task<IActionResult> LeaveBoard(string id, CancellationToken cancellationToken)
        {
            return _leaveBoardHandler.HandleAsync(id, User, cancellationToken);
        }

        [HttpPut("{id}")]
        public Task<IActionResult> UpdateBoard(string id, [FromBody] UpdateBoardDto dto, CancellationToken cancellationToken)
        {
            return _updateBoardHandler.HandleAsync(id, dto, User, cancellationToken);
        }

        [HttpPost("{id}/archive")]
        public Task<IActionResult> ArchiveBoard(string id, CancellationToken cancellationToken)
        {
            return _archiveBoardHandler.HandleAsync(id, User, cancellationToken);
        }

        [HttpPost("{id}/restore")]
        public Task<IActionResult> RestoreBoard(string id, CancellationToken cancellationToken)
        {
            return _restoreBoardHandler.HandleAsync(id, User, cancellationToken);
        }

        [HttpDelete("{id}")]
        public Task<IActionResult> DeleteBoard(string id, CancellationToken cancellationToken)
        {
            return _deleteBoardHandler.HandleAsync(id, User, cancellationToken);
        }

        [HttpPost("{boardId}/invite")]
        public Task<IActionResult> InviteUser(string boardId, [FromBody] string username, CancellationToken cancellationToken)
        {
            if (User.IsDemoUser())
            {
                return Task.FromResult(DemoFeatureForbidden());
            }

            return _inviteBoardUserHandler.HandleAsync(boardId, username, User, cancellationToken);
        }

        [HttpPost("{id}/accept")]
        public Task<IActionResult> AcceptInvite(string id, CancellationToken cancellationToken)
        {
            if (User.IsDemoUser())
            {
                return Task.FromResult(DemoFeatureForbidden());
            }

            return _acceptBoardInviteHandler.HandleAsync(id, User, cancellationToken);
        }

        [HttpPost("{id}/decline")]
        public Task<IActionResult> DeclineInvite(string id, CancellationToken cancellationToken)
        {
            if (User.IsDemoUser())
            {
                return Task.FromResult(DemoFeatureForbidden());
            }

            return _declineBoardInviteHandler.HandleAsync(id, User, cancellationToken);
        }

        [HttpGet("{id}/members")]
        public Task<IActionResult> GetBoardMembers(string id, CancellationToken cancellationToken)
        {
            return _getBoardMembersHandler.HandleAsync(id, User, cancellationToken);
        }

        [HttpPost("{id}/members")]
        public Task<IActionResult> AddMember(string id, [FromBody] AddMemberDto dto, CancellationToken cancellationToken)
        {
            if (User.IsDemoUser())
            {
                return Task.FromResult(DemoFeatureForbidden());
            }

            return _addBoardMemberHandler.HandleAsync(id, dto, User, cancellationToken);
        }

        [HttpDelete("{id}/members/{userId}")]
        public Task<IActionResult> RemoveMember(string id, string userId, CancellationToken cancellationToken)
        {
            if (User.IsDemoUser())
            {
                return Task.FromResult(DemoFeatureForbidden());
            }

            return _removeBoardMemberHandler.HandleAsync(id, userId, User, cancellationToken);
        }

        private IActionResult DemoFeatureForbidden()
        {
            var problem = ApiProblemDetailsFactory.Create(
                StatusCodes.Status403Forbidden,
                DemoUserPolicy.RestrictedFeatureMessage,
                HttpContext);

            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
