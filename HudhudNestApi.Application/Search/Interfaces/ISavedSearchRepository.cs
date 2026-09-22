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
}
