using SphereBackend.Features.Auth;
using SphereBackend.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SphereBackend.IntegrationTests;

public sealed class AuthEndpointsSmokeTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointsSmokeTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task Logout_WithoutRefreshCookie_ReturnsOk_AndClearsAuthCookies()
    {
        var response = await _client.PostAsync("/api/auth/logout", content: null);
        var setCookieHeaders = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.ToArray()
            : Array.Empty<string>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(setCookieHeaders, cookie => cookie.StartsWith("refreshToken=", StringComparison.Ordinal));
        Assert.Contains(setCookieHeaders, cookie => cookie.StartsWith("__session_hint=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateSession_WithoutRefreshCookie_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/auth/session");
        var body = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status401Unauthorized, body!.Status);
        Assert.Equal("Session expired.", body.Detail);
    }

    [Fact]
    public async Task Login_WhenRateLimited_ReturnsRetryMetadata()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            db.Users.Add(new User
            {
                Username = "alice",
                Email = "alice@example.com",
                NormalizedUsername = "alice",
                NormalizedEmail = "alice@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("CorrectPassword123!"),
                IsEmailConfirmed = true
            });
        });

        var request = new LoginRequest
        {
            Identifier = "alice@example.com",
            Password = "wrong-password"
        };

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var unauthorizedResponse = await _client.PostAsJsonAsync("/api/auth/login", request);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);
        }

        var response = await _client.PostAsJsonAsync("/api/auth/login", request);
        var body = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status429TooManyRequests, body!.Status);
        Assert.Equal("Too Many Requests", body.Title);
        Assert.True(body.Extensions.TryGetValue("retryAfterSeconds", out var retryAfterSecondsValue));
        Assert.True(body.Extensions.TryGetValue("retryAfterUtc", out var retryAfterUtcValue));

        var retryAfterSecondsJson = Assert.IsType<JsonElement>(retryAfterSecondsValue);
        Assert.True(retryAfterSecondsJson.TryGetInt32(out var retryAfterSeconds));
        Assert.InRange(retryAfterSeconds, 1, 60);

        var retryAfterUtcJson = Assert.IsType<JsonElement>(retryAfterUtcValue);
        var retryAfterUtcText = retryAfterUtcJson.GetString();
        Assert.False(string.IsNullOrWhiteSpace(retryAfterUtcText));
        Assert.True(
            DateTime.TryParse(
                retryAfterUtcText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var retryAfterUtc));
        Assert.True(retryAfterUtc > DateTime.UtcNow);

        Assert.True(response.Headers.TryGetValues("Retry-After", out var retryAfterHeaders));
        Assert.Equal(retryAfterSeconds.ToString(CultureInfo.InvariantCulture), Assert.Single(retryAfterHeaders));
    }
}
