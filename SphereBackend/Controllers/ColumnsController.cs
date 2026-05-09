using SphereBackend.Features.Columns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SphereBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ColumnsController : ControllerBase
    {
        private readonly IReorderColumnsHandler _reorderColumnsHandler;
        private readonly ICreateColumnHandler _createColumnHandler;
        private readonly IUpdateColumnHandler _updateColumnHandler;
        private readonly IRestoreColumnHandler _restoreColumnHandler;
        private readonly IDeleteColumnForeverHandler _deleteColumnForeverHandler;

        public ColumnsController(
            IReorderColumnsHandler reorderColumnsHandler,
            ICreateColumnHandler createColumnHandler,
            IUpdateColumnHandler updateColumnHandler,
            IRestoreColumnHandler restoreColumnHandler,
            IDeleteColumnForeverHandler deleteColumnForeverHandler)
        {
            _reorderColumnsHandler = reorderColumnsHandler;
            _createColumnHandler = createColumnHandler;
            _updateColumnHandler = updateColumnHandler;
            _restoreColumnHandler = restoreColumnHandler;
            _deleteColumnForeverHandler = deleteColumnForeverHandler;
        }

        [HttpPut("reorder")]
        public Task<IActionResult> ReorderColumns([FromBody] ReorderColumnsRequestDto request, CancellationToken cancellationToken)
        {
            return _reorderColumnsHandler.HandleAsync(request, User, cancellationToken);
        }

        [HttpPost]
        public Task<ActionResult<ColumnShortDto>> CreateColumn([FromBody] CreateColumnDto dto, CancellationToken cancellationToken)
        {
            return _createColumnHandler.HandleAsync(dto, User, cancellationToken);
        }

        [HttpPatch("{id}")]
        public Task<IActionResult> UpdateColumn(string id, [FromBody] UpdateColumnDto dto, CancellationToken cancellationToken)
        {
            return _updateColumnHandler.HandleAsync(id, dto, User, cancellationToken);
        }

        [HttpPost("{id}/restore")]
        public Task<IActionResult> RestoreColumn(string id, CancellationToken cancellationToken)
        {
            return _restoreColumnHandler.HandleAsync(id, User, cancellationToken);
        }

        [HttpDelete("{id}/permanent")]
        public Task<IActionResult> DeleteColumnForever(string id, CancellationToken cancellationToken)
        {
            return _deleteColumnForeverHandler.HandleAsync(id, User, cancellationToken);
        }
    }
}
