using SphereBackend.Features.Boards;
using SphereBackend.Models;

namespace UnitTests;

public class BoardFeatureHelpersTests
{
    [Theory]
    [InlineData("alice_123")]
    [InlineData("Alice-User")]
    [InlineData("USER123")]
    public void IsAsciiUsername_ValidUsername_ReturnsTrue(string username)
    {
        var result = BoardFeatureHelpers.IsAsciiUsername(username);

        Assert.True(result);
    }

    [Theory]
    [InlineData("john doe")]
    [InlineData("иван")]
    [InlineData("john!")]
    public void IsAsciiUsername_InvalidUsername_ReturnsFalse(string username)
    {
        var result = BoardFeatureHelpers.IsAsciiUsername(username);

        Assert.False(result);
    }

    [Fact]
    public void AssignSequentialBoardMemberOrders_RewritesOrdersFromZero()
    {
        var members = new List<BoardMember>
        {
            new() { Order = 10 },
            new() { Order = 25 },
            new() { Order = 99 }
        };

        BoardFeatureHelpers.AssignSequentialBoardMemberOrders(members);

        Assert.Equal(0, members[0].Order);
        Assert.Equal(1, members[1].Order);
        Assert.Equal(2, members[2].Order);
    }
}
