namespace SphereBackend.Features.Users.SearchUsers
{
    public sealed class SearchUsersQuery
    {
        public string? SearchText { get; init; }
        public int CurrentUserId { get; init; }
    }
}
