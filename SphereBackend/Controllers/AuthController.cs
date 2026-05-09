using SphereBackend.Features.Auth;
using Microsoft.AspNetCore.Mvc;

namespace SphereBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IRegisterHandler _registerHandler;
        private readonly IConfirmEmailHandler _confirmEmailHandler;
        private readonly IResendConfirmationHandler _resendConfirmationHandler;
        private readonly ILoginHandler _loginHandler;
        private readonly ILogoutHandler _logoutHandler;
        private readonly IValidateSessionHandler _validateSessionHandler;
        private readonly IRefreshSessionHandler _refreshSessionHandler;
        private readonly ICreateDemoSessionHandler _createDemoSessionHandler;

        public AuthController(
            IRegisterHandler registerHandler,
            IConfirmEmailHandler confirmEmailHandler,
            IResendConfirmationHandler resendConfirmationHandler,
            ILoginHandler loginHandler,
            ILogoutHandler logoutHandler,
            IValidateSessionHandler validateSessionHandler,
            IRefreshSessionHandler refreshSessionHandler,
            ICreateDemoSessionHandler createDemoSessionHandler)
        {
            _registerHandler = registerHandler;
            _confirmEmailHandler = confirmEmailHandler;
            _resendConfirmationHandler = resendConfirmationHandler;
            _loginHandler = loginHandler;
            _logoutHandler = logoutHandler;
            _validateSessionHandler = validateSessionHandler;
            _refreshSessionHandler = refreshSessionHandler;
            _createDemoSessionHandler = createDemoSessionHandler;
        }

        [HttpPost("register")]
        public Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
        {
            return _registerHandler.HandleAsync(request, HttpContext, cancellationToken);
        }

        [HttpGet("confirm-email")]
        public Task<IActionResult> ConfirmEmail(string token, CancellationToken cancellationToken)
        {
            return _confirmEmailHandler.HandleAsync(token, HttpContext, cancellationToken);
        }

        [HttpPost("resend-confirmation")]
        public Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequest request, CancellationToken cancellationToken)
        {
            return _resendConfirmationHandler.HandleAsync(request, cancellationToken);
        }

        [HttpPost("login")]
        public Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
        {
            return _loginHandler.HandleAsync(request, HttpContext, cancellationToken);
        }

        [HttpPost("logout")]
        public Task<IActionResult> Logout(CancellationToken cancellationToken)
        {
            return _logoutHandler.HandleAsync(HttpContext, cancellationToken);
        }

        [HttpGet("session")]
        [HttpGet("validate")]
        public Task<IActionResult> ValidateSession(CancellationToken cancellationToken)
        {
            return _validateSessionHandler.HandleAsync(HttpContext, cancellationToken);
        }

        [HttpPost("refresh")]
        public Task<IActionResult> Refresh(CancellationToken cancellationToken)
        {
            return _refreshSessionHandler.HandleAsync(HttpContext, cancellationToken);
        }

        [HttpPost("demo")]
        public Task<IActionResult> CreateDemoSession(CancellationToken cancellationToken)
        {
            return _createDemoSessionHandler.HandleAsync(HttpContext, cancellationToken);
        }
    }
}
