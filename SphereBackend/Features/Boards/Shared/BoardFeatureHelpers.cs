using SphereBackend.Models;

namespace SphereBackend.Features.Boards
{
    internal static class BoardFeatureHelpers
    {
        public static bool IsAsciiUsername(string value)
        {
            return value.All(ch =>
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= 'a' && ch <= 'z') ||
                (ch >= '0' && ch <= '9') ||
                ch == '_' ||
                ch == '-');
        }

        public static void AssignSequentialBoardMemberOrders(IList<BoardMember> members)
        {
            for (var index = 0; index < members.Count; index++)
            {
                members[index].Order = index;
            }
        }
    }
}
