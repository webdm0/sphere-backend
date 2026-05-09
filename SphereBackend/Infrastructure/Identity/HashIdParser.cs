using SphereBackend.Services;

namespace SphereBackend.Extensions
{
    public static class HashIdParser
    {
        public static bool TryDecode(IIdHasher idHasher, string? value, out int result)
        {
            result = 0;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            return idHasher.TryDecode(value, out result);
        }

        public static bool TryDecodeNullable(IIdHasher idHasher, string? value, out int? result)
        {
            if (value is null)
            {
                result = null;
                return true;
            }

            if (!TryDecode(idHasher, value, out var decoded))
            {
                result = null;
                return false;
            }

            result = decoded;
            return true;
        }

        public static bool TryDecodeMany(IIdHasher idHasher, IEnumerable<string> values, out List<int> decoded)
        {
            decoded = new List<int>();

            foreach (var value in values)
            {
                if (!TryDecode(idHasher, value, out var id))
                {
                    decoded.Clear();
                    return false;
                }

                decoded.Add(id);
            }

            return true;
        }
    }
}
