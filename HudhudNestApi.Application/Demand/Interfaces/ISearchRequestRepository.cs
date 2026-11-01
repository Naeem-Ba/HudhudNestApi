using HudhudNestApi.Application.Demand.DTOs;
using HudhudNestApi.Application.Demand.Matching;
using HudhudNestApi.Application.Listings.Currencies;
using HudhudNestApi.Domain.Demand.Entities;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Demand.Interfaces;

public interface ISearchRequestRepository
{
    void Add(PropertySearchRequest request);
    void AddMatch(SearchRequestMatch match);

    /// <summary>Tracked; null when it does not exist or belongs to someone else.</summary>
    Task<PropertySearchRequest?> GetOwnedAsync(Guid id, Guid userId, CancellationToken ct = default);

    /// <summary>Tracked; null unless the request is active and not past its end date.</summary>
    Task<PropertySearchRequest?> GetActiveAsync(Guid id, DateTime nowUtc, CancellationToken ct = default);

    Task<IReadOnlyList<(PropertySearchRequest Request, int MatchCount)>> ListByUserAsync(Guid userId, int minScore, CancellationToken ct = default);
    Task<int> CountOpenByUserAsync(Guid userId, CancellationToken ct = default);

    Task<PagedSearchRequests> GetBoardAsync(ListingType? listingType, int? governorateId, int? propertyTypeId, DateTime nowUtc, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Active requests that could fit a listing: same governorate and request type. Bounded by <paramref name="take"/>.</summary>
    Task<IReadOnlyList<PropertySearchRequest>> GetCandidatesForListingAsync(ListingFacts listing, DateTime nowUtc, int take, CancellationToken ct = default);

    Task<IReadOnlyList<SearchRequestMatchDto>> GetMatchesAsync(Guid requestId, DateTime nowUtc, int minScore, int take, CancellationToken ct = default);
    Task<SearchRequestMatch?> GetMatchAsync(Guid requestId, Guid propertyId, CancellationToken ct = default);
    Task<IReadOnlySet<Guid>> GetProposedRequestIdsAsync(Guid propertyId, CancellationToken ct = default);
    Task<int> CountProposalsSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken ct = default);

    /// <summary>Facts of a listing that is on the market now, or null.</summary>
    Task<ListingFacts?> GetListingFactsAsync(Guid propertyId, DateTime nowUtc, CancellationToken ct = default);
    Task<string?> GetListingTitleAsync(Guid propertyId, CancellationToken ct = default);

    /// <summary>True when the user has at least one listing on the market (advertisers only see the board).</summary>
    Task<bool> HasListingOnMarketAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default);

    Task<CurrencyRates> LoadRatesAsync(CancellationToken ct = default);
}
