using SphereBackend.Extensions;
using System.Security.Claims;
using UnitTests.TestDoubles;

namespace UnitTests;

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void GetCurrentUserId_WithValidClaim_ReturnsDecodedId()
    {
        var user = CreatePrincipal("123");

        var result = user.GetCurrentUserId(new StubIdHasher());

        Assert.Equal(123, result);
    }

    [Fact]
    public void GetCurrentUserId_WhenClaimIsMissing_Throws()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        var exception = Assert.Throws<InvalidOperationException>(() => user.GetCurrentUserId(new StubIdHasher()));

        Assert.Equal("User ID claim is missing", exception.Message);
    }

    [Fact]
    public void GetCurrentUserId_WhenClaimIsInvalid_Throws()
    {
        var user = CreatePrincipal("abc");

        var exception = Assert.Throws<InvalidOperationException>(() => user.GetCurrentUserId(new StubIdHasher()));

        Assert.Equal("User ID claim is invalid", exception.Message);
    }

    [Fact]
    public void IsDemoUser_WithTrueClaim_ReturnsTrue()
    {
        var user = CreatePrincipal("123", isDemo: true);

        var result = user.IsDemoUser();

        Assert.True(result);
    }

    [Fact]
    public void IsDemoUser_WithoutClaim_ReturnsFalse()
    {
        var user = CreatePrincipal("123");

        var result = user.IsDemoUser();

        Assert.False(result);
    }

    private static ClaimsPrincipal CreatePrincipal(string? userId, bool isDemo = false)
    {
        var claims = new List<Claim>();
        if (userId is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        }

        if (isDemo)
        {
            claims.Add(new Claim("is_demo", bool.TrueString));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
