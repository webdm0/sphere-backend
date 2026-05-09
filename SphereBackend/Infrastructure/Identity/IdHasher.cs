using HashidsNet;

namespace SphereBackend.Services
{
    public interface IIdHasher
    {
        string Encode(int id);
        int Decode(string hash);
        bool TryDecode(string? hash, out int id);
    }

    public class IdHasher : IIdHasher
    {
        private readonly IHashids _hashids;

        public IdHasher(IHashids hashids)
        {
            _hashids = hashids;
        }

        public string Encode(int id) => _hashids.Encode(id);

        public int Decode(string hash)
        {
            return TryDecode(hash, out var id) ? id : 0;
        }

        public bool TryDecode(string? hash, out int id)
        {
            id = 0;

            if (string.IsNullOrWhiteSpace(hash))
                return false;

            try
            {
                var decoded = _hashids.Decode(hash);

                if (decoded.Length != 1 || decoded[0] <= 0)
                    return false;

                id = decoded[0];
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
