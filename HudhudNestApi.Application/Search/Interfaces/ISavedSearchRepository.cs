using HudhudNestApi.Domain.Search.Entities;

namespace HudhudNestApi.Application.Search.Interfaces;

public interface ISavedSearchRepository
{
    void Add(SavedSearch savedSearch);
    void Remove(SavedSearch savedSearch);

    Task<SavedSearch?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SavedSearch>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Used by SavedSearchMatchHostedService to sweep every saved search each run.</summary>
    Task<IReadOnlyList<SavedSearch>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// P18-D04: tracked saved searches in stable Id order, at most <paramref name="batchSize"/> of them
    /// after <paramref name="afterId"/> (null = from the start). The matching sweep reads them in
    /// batches so memory stays bounded however many searches users have saved.
    /// </summary>
    Task<IReadOnlyList<SavedSearch>> GetBatchAsync(Guid? afterId, int batchSize, CancellationToken ct = default);
}
