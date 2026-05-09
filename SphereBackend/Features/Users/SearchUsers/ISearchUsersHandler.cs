namespace SphereBackend.Features.Users.SearchUsers
{
    public interface ISearchUsersHandler
    {
        Task<IReadOnlyList<SearchUserItem>> HandleAsync(
            SearchUsersQuery query,
            CancellationToken cancellationToken = default);
    }
}
