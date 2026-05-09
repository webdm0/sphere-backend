using HashidsNet;
using SphereBackend.Services;

namespace UnitTests;

public class IdHasherTests
{
    private readonly IdHasher _sut = new(new Hashids("unit-test-salt", 8));

    [Fact]
    public void Encode_And_TryDecode_ReturnsOriginalId()
    {
        const int originalId = 123;

        var hash = _sut.Encode(originalId);
        var success = _sut.TryDecode(hash, out var decodedId);

        Assert.True(success);
        Assert.Equal(originalId, decodedId);
    }

    [Fact]
    public void TryDecode_InvalidHash_ReturnsFalse_AndZeroId()
    {
        var success = _sut.TryDecode("not-a-valid-hash", out var decodedId);

        Assert.False(success);
        Assert.Equal(0, decodedId);
    }
}
