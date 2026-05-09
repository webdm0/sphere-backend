using SphereBackend.Extensions;
using SphereBackend.Features.Auth;
using SphereBackend.Features.Users.SearchUsers;
using SphereBackend.Infrastructure.Http;
using SphereBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SphereBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly ISearchUsersHandler _searchUsersHandler;
        private readonly IIdHasher _idHasher;

        public UsersController(ISearchUsersHandler searchUsersHandler, IIdHasher idHasher)
        {
            _searchUsersHandler = searchUsersHandler;
            _idHasher = idHasher;
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchUsers([FromQuery] string? query, CancellationToken cancellationToken)
        {
            if (User.IsDemoUser())
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

            var currentUserId = User.GetCurrentUserId(_idHasher);

            var response = await _searchUsersHandler.HandleAsync(
                new SearchUsersQuery
                {
                    SearchText = query,
                    CurrentUserId = currentUserId
                },
                cancellationToken);

            return Ok(response);
        }
    }
}

