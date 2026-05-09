using SphereBackend.Extensions;
using UnitTests.TestDoubles;

namespace UnitTests;

public class HashIdParserTests
{
    private readonly StubIdHasher _idHasher = new();

    [Fact]
    public void TryDecode_WithValidValue_ReturnsTrueAndDecodedId()
    {
        var success = HashIdParser.TryDecode(_idHasher, "55", out var result);

        Assert.True(success);
        Assert.Equal(55, result);
    }

    [Fact]
    public void TryDecode_WithInvalidValue_ReturnsFalse()
    {
        var success = HashIdParser.TryDecode(_idHasher, "bad", out var result);

        Assert.False(success);
        Assert.Equal(0, result);
    }

    [Fact]
    public void TryDecodeNullable_WithNullValue_ReturnsTrueAndNull()
    {
        var success = HashIdParser.TryDecodeNullable(_idHasher, null, out var result);

        Assert.True(success);
        Assert.Null(result);
    }

    [Fact]
    public void TryDecodeMany_WithAllValidValues_ReturnsTrueAndDecodedIds()
    {
        var success = HashIdParser.TryDecodeMany(_idHasher, new[] { "1", "2", "3" }, out var result);

        Assert.True(success);
        Assert.Equal(new[] { 1, 2, 3 }, result);
    }

    [Fact]
    public void TryDecodeMany_WithInvalidValue_ReturnsFalseAndClearsResult()
    {
        var success = HashIdParser.TryDecodeMany(_idHasher, new[] { "1", "bad", "3" }, out var result);

        Assert.False(success);
        Assert.Empty(result);
    }
}
