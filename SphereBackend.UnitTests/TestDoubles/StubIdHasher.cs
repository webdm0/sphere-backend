using SphereBackend.Services;

namespace UnitTests.TestDoubles;

internal sealed class StubIdHasher : IIdHasher
{
    public string Encode(int id) => id.ToString();

    public int Decode(string hash) => int.TryParse(hash, out var value) ? value : 0;

    public bool TryDecode(string? hash, out int id) => int.TryParse(hash, out id);
}
