using SphereBackend.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace SphereBackend.IntegrationTests;

public sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "IntegrationTest";
    public const string UserIdHeaderName = "X-Test-UserId";
    public const string IsDemoHeaderName = "X-Test-IsDemo";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdHeaderName, out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!int.TryParse(values.ToString(), out var userId) || userId <= 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid test user id."));
        }

        var idHasher = Context.RequestServices.GetRequiredService<IIdHasher>();
        var encodedUserId = idHasher.Encode(userId);
        var isDemo = Request.Headers.TryGetValue(IsDemoHeaderName, out var isDemoValues) &&
            bool.TryParse(isDemoValues.ToString(), out var parsedIsDemo) &&
            parsedIsDemo;
        Claim[] claims =
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, encodedUserId),
            new System.Security.Claims.Claim("username", $"test-user-{userId}"),
            new System.Security.Claims.Claim("email", $"test{userId}@example.com"),
            new System.Security.Claims.Claim("is_demo", isDemo ? bool.TrueString : bool.FalseString)
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
