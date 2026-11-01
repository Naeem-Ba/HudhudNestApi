using HudhudNestApi.Application.Demand.DTOs;
using HudhudNestApi.Application.Demand.Interfaces;
using HudhudNestApi.Application.Demand.Matching;
using HudhudNestApi.Application.Listings.Currencies;
using HudhudNestApi.Domain.Demand.Entities;
using HudhudNestApi.Domain.Demand.Enums;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HudhudNestApi.Infrastructure.Demand;

public sealed class SearchRequestRepository : ISearchRequestRepository
{
    private readonly AppDbContext _db;

    public SearchRequestRepository(AppDbContext db) => _db = db;

    public void Add(PropertySearchRequest request) => _db.PropertySearchRequests.Add(request);

    public void AddMatch(SearchRequestMatch match) => _db.SearchRequestMatches.Add(match);

    public Task<PropertySearchRequest?> GetOwnedAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _db.PropertySearchRequests.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);

    public Task<PropertySearchRequest?> GetActiveAsync(Guid id, DateTime nowUtc, CancellationToken ct = default)
        => _db.PropertySearchRequests.FirstOrDefaultAsync(
            r => r.Id == id && r.Status == SearchRequestStatus.Active && r.ExpiresAt > nowUtc, ct);

    public async Task<IReadOnlyList<(PropertySearchRequest Request, int MatchCount)>> ListByUserAsync(Guid userId, int minScore, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var rows = await _db.PropertySearchRequests
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .Select(r => new
            {
                Request = r,
                Matches = _db.SearchRequestMatches.Count(m => m.SearchRequestId == r.Id && m.Score >= minScore &&
                    _db.Properties.Where(Property.OnMarket(now)).Any(p => p.Id == m.PropertyId)),
            })
            .ToListAsync(ct);

        return rows.Select(x => (x.Request, x.Matches)).ToList();
    }

    public Task<int> CountOpenByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return _db.PropertySearchRequests.CountAsync(r =>
            r.UserId == userId &&
            (r.Status == SearchRequestStatus.Active || r.Status == SearchRequestStatus.Paused) &&
            r.ExpiresAt > now, ct);
    }

    public async Task<PagedSearchRequests> GetBoardAsync(
        ListingType? listingType, int? governorateId, int? propertyTypeId, DateTime nowUtc, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.PropertySearchRequests
            .AsNoTracking()
            .Where(r => r.Status == SearchRequestStatus.Active && r.ExpiresAt > nowUtc);

        if (listingType is ListingType.ForSale or ListingType.ForRent)
            query = query.Where(r => r.ListingType == listingType);
        if (governorateId.HasValue)
            query = query.Where(r => r.GovernorateId == governorateId);
        if (propertyTypeId.HasValue)
            query = query.Where(r => r.PropertyTypeId == null || r.PropertyTypeId == propertyTypeId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedSearchRequests(items.Select(AnonymousSearchRequestDto.From).ToList(), total, page, pageSize);
    }

    public async Task<IReadOnlyList<PropertySearchRequest>> GetCandidatesForListingAsync(
        ListingFacts listing, DateTime nowUtc, int take, CancellationToken ct = default)
    {
        if (listing.GovernorateId is null)
            return [];

        var types = listing.ListingType == ListingType.ForRentAndSale
            ? new[] { ListingType.ForSale, ListingType.ForRent }
            : new[] { listing.ListingType };

        return await _db.PropertySearchRequests
            .AsNoTracking()
            .Where(r => r.Status == SearchRequestStatus.Active && r.ExpiresAt > nowUtc)
            .Where(r => r.GovernorateId == listing.GovernorateId && types.Contains(r.ListingType))
            .Where(r => r.PropertyTypeId == null || r.PropertyTypeId == listing.PropertyTypeId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SearchRequestMatchDto>> GetMatchesAsync(Guid requestId, DateTime nowUtc, int minScore, int take, CancellationToken ct = default)
    {
        var rows = await _db.SearchRequestMatches
            .AsNoTracking()
            .Where(m => m.SearchRequestId == requestId && m.Score >= minScore)
            .Join(_db.Properties.Where(Property.OnMarket(nowUtc)), m => m.PropertyId, p => p.Id, (m, p) => new
            {
                m.PropertyId,
                p.Title,
                p.ListingType,
                p.PurchasePrice,
                p.ColdRent,
                p.WarmRent,
                p.CurrencyCode,
                p.Rooms,
                p.Area,
                Image = p.Images.Where(i => i.IsMain && !i.IsDeleted).Select(i => i.ThumbnailUrl ?? i.Url).FirstOrDefault(),
                m.Score,
                m.Reasons,
                m.Source,
                m.CreatedAt,
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        return rows.Select(x => new SearchRequestMatchDto(
            x.PropertyId, x.Title, x.ListingType.ToString(), x.PurchasePrice, x.ColdRent, x.WarmRent, x.CurrencyCode.Trim(),
            x.Rooms, x.Area, x.Image, x.Score,
            x.Reasons.Length == 0 ? [] : x.Reasons.Split(',', StringSplitOptions.RemoveEmptyEntries),
            x.Source.ToString(), x.CreatedAt)).ToList();
    }

    public Task<SearchRequestMatch?> GetMatchAsync(Guid requestId, Guid propertyId, CancellationToken ct = default)
        => _db.SearchRequestMatches.FirstOrDefaultAsync(m => m.SearchRequestId == requestId && m.PropertyId == propertyId, ct);

    public async Task<IReadOnlySet<Guid>> GetProposedRequestIdsAsync(Guid propertyId, CancellationToken ct = default)
        => (await _db.SearchRequestMatches
                .AsNoTracking()
                .Where(m => m.PropertyId == propertyId && m.ProposedAt != null)
                .Select(m => m.SearchRequestId)
                .ToListAsync(ct))
            .ToHashSet();

    public Task<int> CountProposalsSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken ct = default)
        => _db.SearchRequestMatches.IgnoreQueryFilters()
            .CountAsync(m => m.ProposedByUserId == userId && m.ProposedAt >= sinceUtc, ct);

    public Task<ListingFacts?> GetListingFactsAsync(Guid propertyId, DateTime nowUtc, CancellationToken ct = default)
        // Filter before the projection: EF cannot translate a filter on a constructor-built record.
        => FactsQuery(_db, nowUtc, p => p.Id == propertyId).FirstOrDefaultAsync(ct);

    public Task<string?> GetListingTitleAsync(Guid propertyId, CancellationToken ct = default)
        => _db.Properties.AsNoTracking().Where(p => p.Id == propertyId).Select(p => p.Title).FirstOrDefaultAsync(ct);

    public Task<bool> HasListingOnMarketAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default)
        => _db.Properties.AsNoTracking().Where(Property.OnMarket(nowUtc)).AnyAsync(p => p.OwnerId == userId, ct);

    public Task<CurrencyRates> LoadRatesAsync(CancellationToken ct = default)
        => PropertyRepository.LoadCurrencyRatesAsync(_db, ct);

    /// <summary>Listings on the market, projected to what the matcher reads. Shared with the background sweep.</summary>
    internal static IQueryable<ListingFacts> FactsQuery(
        AppDbContext db, DateTime nowUtc, System.Linq.Expressions.Expression<Func<Property, bool>>? filter = null)
    {
        var query = db.Properties.AsNoTracking().Where(Property.OnMarket(nowUtc));
        if (filter is not null)
            query = query.Where(filter);
        return query.Select(p => new ListingFacts(
                p.Id, p.OwnerId, p.ListingType, p.GovernorateId, p.DistrictId, p.NeighborhoodId, p.PropertyTypeId,
                p.PurchasePrice,
                p.WarmRent ?? p.ColdRent,
                p.CurrencyCode.Trim(),
                p.Rooms, p.Area, p.HasBalcony, p.HasElevator, p.HasParkingSpace, p.FurnishingStatus));
    }
}
