using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Search.Commands.CreateSavedSearch;
using HudhudNestApi.Application.Search.Commands.DeleteSavedSearch;
using HudhudNestApi.Application.Search.Interfaces;
using HudhudNestApi.Domain.Search.Entities;

namespace HudhudNestApi.Application.Tests.Search;

public sealed class SavedSearchCommandHandlerTests
{
    [Fact]
    public async Task CreateSavedSearch_PersistsAndReturnsNewId()
    {
        var repo = new InMemorySavedSearchRepository();
        var handler = new CreateSavedSearchCommandHandler(repo, new NoOpUnitOfWork());
        var userId = Guid.NewGuid();

        var id = await handler.Handle(
            new CreateSavedSearchCommand(
                userId, "My search", "SY", null, null,
                GovernorateId: 1, DistrictId: null, NeighborhoodId: 5, PropertyTypeId: null,
                ListingType: null, MinPrice: null, MaxPrice: 500m, CurrencyCode: "USD",
                MinRooms: 2, MaxRooms: null, MinArea: null, MaxArea: null),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
        Assert.Single(repo.Items);
        Assert.Equal(userId, repo.Items[0].UserId);
    }

    [Fact]
    public async Task DeleteSavedSearch_ReturnsFalse_WhenNotOwnedByRequestingUser()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var existing = SavedSearch.Create(owner, "mine", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        var repo = new InMemorySavedSearchRepository(existing);
        var handler = new DeleteSavedSearchCommandHandler(repo, new NoOpUnitOfWork());

        var result = await handler.Handle(new DeleteSavedSearchCommand(existing.Id, stranger), CancellationToken.None);

        Assert.False(result);
        Assert.Single(repo.Items);
    }

    [Fact]
    public async Task DeleteSavedSearch_RemovesIt_WhenOwnedByRequestingUser()
    {
        var owner = Guid.NewGuid();
        var existing = SavedSearch.Create(owner, "mine", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        var repo = new InMemorySavedSearchRepository(existing);
        var handler = new DeleteSavedSearchCommandHandler(repo, new NoOpUnitOfWork());

        var result = await handler.Handle(new DeleteSavedSearchCommand(existing.Id, owner), CancellationToken.None);

        Assert.True(result);
        Assert.Empty(repo.Items);
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IReadOnlyList<object>> SaveChangesDroppingConcurrencyConflictsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        public Task BeginTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task AcquireAdvisoryLockAsync(long key, CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose()
        {
        }
    }

    private sealed class InMemorySavedSearchRepository : ISavedSearchRepository
    {
        public List<SavedSearch> Items { get; } = new();

        public InMemorySavedSearchRepository(params SavedSearch[] seed) => Items.AddRange(seed);

        public void Add(SavedSearch savedSearch) => Items.Add(savedSearch);
        public void Remove(SavedSearch savedSearch) => Items.RemoveAll(s => s.Id == savedSearch.Id);

        public Task<SavedSearch?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<SavedSearch>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SavedSearch>>(Items.Where(s => s.UserId == userId).ToList());

        public Task<IReadOnlyList<SavedSearch>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SavedSearch>>(Items.ToList());
    }
}
