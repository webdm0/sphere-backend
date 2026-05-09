using SphereBackend.Models;

namespace SphereBackend.Features.Columns
{
    internal static class ColumnFeatureHelpers
    {
        public static bool HasDuplicateIds(IEnumerable<int> ids)
        {
            var seen = new HashSet<int>();
            foreach (var id in ids)
            {
                if (!seen.Add(id))
                {
                    return true;
                }
            }

            return false;
        }

        public static void AssignSequentialOrders(IList<Column> columns)
        {
            for (var index = 0; index < columns.Count; index++)
            {
                columns[index].Order = index;
            }
        }
    }
}
