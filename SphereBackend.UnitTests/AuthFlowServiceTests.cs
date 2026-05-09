using SphereBackend.Features.Auth;
using SphereBackend.Models;
using SphereBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace UnitTests;

public class AuthFlowServiceTests
{
    [Fact]
    public void NormalizeForStorage_TrimsValue()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(cache);

        var result = sut.NormalizeForStorage("  Alice  ");

        Assert.Equal("Alice", result);
    }

    [Fact]
    public void NormalizeForStorage_Null_ReturnsEmptyString()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(cache);

        var result = sut.NormalizeForStorage(null!);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void NormalizeForLookup_TrimsAndLowercasesValue()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(cache);

        var result = sut.NormalizeForLookup("  Alice-USER  ");

        Assert.Equal("alice-user", result);
    }

    [Fact]
    public void HashRefresh_SameToken_ReturnsSameHash()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(cache);

        var first = sut.HashRefresh("refresh-token");
        var second = sut.HashRefresh("refresh-token");

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }

    [Fact]
    public void GetBanInfo_WhenCacheIsEmpty_ReturnsDefaultState()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(cache);

        var result = sut.GetBanInfo("login_limit_127.0.0.1");

        Assert.Equal(0, result.Attempts);
        Assert.Null(result.BannedUntil);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    public void RegisterAttempt_BeforeThreshold_DoesNotSetBan(int startingAttempts, int expectedAttempts)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(cache);
        var info = new UserBanInfo { Attempts = startingAttempts };

        sut.RegisterAttempt("login_limit_127.0.0.1", info);

        Assert.Equal(expectedAttempts, info.Attempts);
        Assert.Null(info.BannedUntil);

        var cachedInfo = sut.GetBanInfo("login_limit_127.0.0.1");
        Assert.Equal(expectedAttempts, cachedInfo.Attempts);
        Assert.Null(cachedInfo.BannedUntil);
    }

    [Theory]
    [InlineData(2, 3, 1)]
    [InlineData(3, 4, 5)]
    [InlineData(4, 5, 15)]
    public void RegisterAttempt_OnThreshold_SetsExpectedBanWindow(
        int startingAttempts,
        int expectedAttempts,
        int expectedBanMinutes)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(cache);
        var info = new UserBanInfo { Attempts = startingAttempts };
        var before = DateTime.UtcNow;

        sut.RegisterAttempt("login_limit_127.0.0.1", info);

        var after = DateTime.UtcNow;

        Assert.Equal(expectedAttempts, info.Attempts);
        Assert.NotNull(info.BannedUntil);
        Assert.InRange(
            info.BannedUntil!.Value,
            before.AddMinutes(expectedBanMinutes),
            after.AddMinutes(expectedBanMinutes).AddSeconds(5));

        var cachedInfo = sut.GetBanInfo("login_limit_127.0.0.1");
        Assert.Equal(expectedAttempts, cachedInfo.Attempts);
        Assert.Equal(info.BannedUntil, cachedInfo.BannedUntil);
    }

    [Fact]
    public void IsConfirmationEmailCooldownActive_ReturnsFalse_WhenNoPreviousEmailWasSent()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(cache);

        var result = sut.IsConfirmationEmailCooldownActive(new User());

        Assert.False(result);
    }

    [Fact]
    public void IsConfirmationEmailCooldownActive_ReturnsTrue_WhenEmailWasSentRecently()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(cache);

        var result = sut.IsConfirmationEmailCooldownActive(new User
        {
            LastConfirmationEmailSentAt = DateTime.UtcNow.AddMinutes(-1)
        });

        Assert.True(result);
    }

    [Fact]
    public void IsConfirmationEmailCooldownActive_ReturnsFalse_WhenCooldownElapsed()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(cache);

        var result = sut.IsConfirmationEmailCooldownActive(new User
        {
            LastConfirmationEmailSentAt = DateTime.UtcNow.AddMinutes(-3)
        });

        Assert.False(result);
    }

    [Fact]
    public void MarkConfirmationEmailSent_SetsCurrentTimestamp()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(cache);
        var user = new User();
        var before = DateTime.UtcNow;

        sut.MarkConfirmationEmailSent(user);

        var after = DateTime.UtcNow;
        Assert.NotNull(user.LastConfirmationEmailSentAt);
        Assert.InRange(user.LastConfirmationEmailSentAt!.Value, before, after.AddSeconds(1));
    }

    [Fact]
    public void GetClientIp_PrefersFirstForwardedIp()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(cache);
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");
        httpContext.Request.Headers["X-Forwarded-For"] = "198.51.100.10, 10.0.0.1";

        var result = sut.GetClientIp(httpContext);

        Assert.Equal("198.51.100.10", result);
    }

    [Fact]
    public void GetClientIp_FallsBackToRemoteIp()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(cache);
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.20");

        var result = sut.GetClientIp(httpContext);

        Assert.Equal("203.0.113.20", result);
    }

    [Fact]
    public void SetSessionHintCookie_DefaultsToLaxSameSite()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(
            cache,
            new Dictionary<string, string?>
            {
                ["SessionHint:Key"] = "session-hint-key-1234567890-abcdef",
                ["SessionHint:Issuer"] = "UnitTests",
                ["SessionHint:Audience"] = "UnitTests"
            });
        var httpContext = new DefaultHttpContext();

        sut.SetSessionHintCookie(httpContext, new UserSession { Id = 42 });

        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains("SameSite=Lax", setCookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetRefreshCookie_DefaultsToLaxSameSite()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(cache);
        var httpContext = new DefaultHttpContext();

        sut.SetRefreshCookie(httpContext, "refresh-token", DateTime.UtcNow.AddDays(7));

        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains("refreshToken=", setCookieHeader, StringComparison.Ordinal);
        Assert.Contains("SameSite=Lax", setCookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetSessionHintCookie_UsesNoneSameSite_ForCrossSiteAuth()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(
            cache,
            new Dictionary<string, string?>
            {
                ["Cookies:UseCrossSiteAuth"] = "true",
                ["SessionHint:Key"] = "session-hint-key-1234567890-abcdef",
                ["SessionHint:Issuer"] = "UnitTests",
                ["SessionHint:Audience"] = "UnitTests"
            });
        var httpContext = new DefaultHttpContext();

        sut.SetSessionHintCookie(httpContext, new UserSession { Id = 42 });

        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains("SameSite=None", setCookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetRefreshCookie_UsesNoneSameSite_ForCrossSiteAuth()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(
            cache,
            new Dictionary<string, string?>
            {
                ["Cookies:UseCrossSiteAuth"] = "true"
            });
        var httpContext = new DefaultHttpContext();

        sut.SetRefreshCookie(httpContext, "refresh-token", DateTime.UtcNow.AddDays(7));

        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains("refreshToken=", setCookieHeader, StringComparison.Ordinal);
        Assert.Contains("SameSite=None", setCookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    private static AuthFlowService CreateSut(
        IMemoryCache cache,
        IDictionary<string, string?>? settings = null)
    {
        return new AuthFlowService(
            context: null!,
            configuration: new ConfigurationBuilder()
                .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
                .Build(),
            env: new TestWebHostEnvironment(),
            cache: cache,
            idHasher: new StubIdHasher());
    }

    private sealed class StubIdHasher : IIdHasher
    {
        public string Encode(int id) => id.ToString();

        public int Decode(string hash) => int.TryParse(hash, out var value) ? value : 0;

        public bool TryDecode(string? hash, out int id) => int.TryParse(hash, out id);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SphereBackend.UnitTests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
