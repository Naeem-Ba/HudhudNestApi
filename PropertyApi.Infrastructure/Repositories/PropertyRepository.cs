using Microsoft.EntityFrameworkCore;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Infrastructure.Repositories;

public sealed class PropertyRepository : IPropertyRepository
{
    private readonly AppDbContext _db;

    public PropertyRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Property property, CancellationToken ct = default)
    {
        await _db.Properties.AddAsync(property, ct);
    }

    public async Task<Property?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Properties
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Property?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Properties
            .Include(p => p.Owner)
            .Include(p => p.Images)
            .Include(p => p.PropertyAmenities)
                .ThenInclude(pa => pa.Amenity)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Property?> GetPublishedByIdWithDetailsAsync(
    Guid id,
    CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _db.Properties
            .AsNoTracking()
            .AsSplitQuery()
            .Include(property => property.Owner)
            .Include(property => property.Images.Where(image => !image.IsDeleted))
            .Include(property => property.PropertyAmenities)
                .ThenInclude(propertyAmenity => propertyAmenity.Amenity)
            .FirstOrDefaultAsync(
                property =>
                    property.Id == id &&
                    property.IsPublished &&
                    (property.ExpiresAt == null ||
                     property.ExpiresAt > now),
                ct);
    }
    public async Task<PagedResult<Property>> GetPagedAsync(
        PropertyFilterDto filter,
        CancellationToken ct = default)
    {
        var query = _db.Properties
            .AsNoTracking()
            .Include(p => p.Owner)
            .Include(p => p.Images.Where(i => i.IsMain))
            .AsQueryable();

        query = ApplyFilter(query, filter, await ResolveLocationFallbackAsync(_db, filter, ct));

        // -- Count (before pagination) -------------------------
        var totalCount = await query.CountAsync(ct);

        // -- Sort ---------------------------------------------
        query = ApplySort(query, filter, DateTime.UtcNow);

        // -- Paginate -----------------------------------------
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Property>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<IReadOnlyList<Property>> GetByOwnerAsync(
        Guid ownerId,
        CancellationToken ct = default)
    {
        return await _db.Properties
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId)
            .Include(p => p.Images.Where(i => i.IsMain))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Properties.AnyAsync(p => p.Id == id, ct);
    }

    public async Task<bool> IsPubliclyVisibleAsync(Guid id, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _db.Properties
            .AsNoTracking()
            .AnyAsync(
                property =>
                    property.Id == id &&
                    property.IsPublished &&
                    (property.ExpiresAt == null || property.ExpiresAt > now),
                ct);
    }

    public async Task<(int SoldCount, int RentedCount)> GetDealCountsByOwnerAsync(
        Guid ownerId,
        CancellationToken ct = default)
    {
        // Two lightweight COUNT queries instead of pulling rows into memory —
        // this only ever needs to run against the public profile page, where
        // the owner can have hundreds of historical listings.
        var soldCount = await _db.Properties
            .AsNoTracking()
            .CountAsync(p => p.OwnerId == ownerId && p.Status == PropertyStatus.Sold, ct);

        var rentedCount = await _db.Properties
            .AsNoTracking()
            .CountAsync(p => p.OwnerId == ownerId && p.Status == PropertyStatus.Rented, ct);

        return (soldCount, rentedCount);
    }

    public async Task<int> CountActiveListingsByOwnerAsync(
        Guid ownerId,
        CancellationToken ct = default)
    {
        // Deleted listings are already excluded by the global query filter on Property
        // (AppDbContext: HasQueryFilter(e => !e.IsDeleted)), so only Expired needs an
        // explicit exclusion here — an expired listing in its grace window must not hold
        // the owner's single free slot hostage.
        return await _db.Properties
            .AsNoTracking()
            .CountAsync(
                p => p.OwnerId == ownerId && p.Status != PropertyStatus.Expired,
                ct);
    }

    public async Task<int> CountActiveListingsByAgencyAsync(
        Guid agencyId,
        CancellationToken ct = default)
    {
        // Mirrors CountActiveListingsByOwnerAsync's "active" definition, but pools every
        // member's listings under the agency instead of a single owner (RELEASE-BLOCKERS-AR.md
        // B-3). AgencyId is stamped on Property at creation time, so no join is needed.
        return await _db.Properties
            .AsNoTracking()
            .CountAsync(
                p => p.AgencyId == agencyId && p.Status != PropertyStatus.Expired,
                ct);
    }

    public async Task<IReadOnlyList<Property>> FindPotentialDuplicatesAsync(
        int neighborhoodId,
        ListingType listingType,
        decimal? price,
        decimal? area,
        decimal maxTolerancePercent,
        CancellationToken ct = default)
    {
        var query = _db.Properties
            .AsNoTracking()
            .Where(p =>
                p.NeighborhoodId == neighborhoodId &&
                p.ListingType == listingType &&
                p.IsPublished);

        // Price tolerance — uses the field relevant to the listing type, same rule
        // GetPagedAsync/ApplyFilter uses for price range filtering.
        if (price.HasValue && price.Value > 0)
        {
            var minPrice = price.Value * (1 - maxTolerancePercent / 100m);
            var maxPrice = price.Value * (1 + maxTolerancePercent / 100m);

            query = listingType switch
            {
                ListingType.ForRent => query.Where(p =>
                    (p.ColdRent >= minPrice && p.ColdRent <= maxPrice) ||
                    (p.WarmRent >= minPrice && p.WarmRent <= maxPrice)),
                ListingType.ForSale => query.Where(p =>
                    p.PurchasePrice >= minPrice && p.PurchasePrice <= maxPrice),
                _ => query.Where(p =>
                    (p.ColdRent >= minPrice && p.ColdRent <= maxPrice) ||
                    (p.WarmRent >= minPrice && p.WarmRent <= maxPrice) ||
                    (p.PurchasePrice >= minPrice && p.PurchasePrice <= maxPrice))
            };
        }

        if (area.HasValue && area.Value > 0)
        {
            var minArea = area.Value * (1 - maxTolerancePercent / 100m);
            var maxArea = area.Value * (1 + maxTolerancePercent / 100m);
            query = query.Where(p => p.Area >= minArea && p.Area <= maxArea);
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .Take(20) // Advisory check only — never needs to return more than a handful of candidates
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<decimal>> GetComparableListingPricesAsync(
        int? propertyTypeId,
        ListingType listingType,
        decimal? area,
        decimal areaTolerancePercent,
        int governorateId,
        int? districtId,
        int? neighborhoodId,
        CancellationToken ct = default)
    {
        var query = ApplyComparableListingsFilter(
            _db.Properties.AsNoTracking(),
            propertyTypeId,
            listingType,
            area,
            areaTolerancePercent,
            governorateId,
            districtId,
            neighborhoodId,
            DateTime.UtcNow);

        // Only the price field relevant to this listing type, resolved server-side — same
        // mapping ResolveComparablePrice (below) expresses for in-memory use, kept as its own
        // SQL-translatable expression here the same way Property.CurrentlyFeatured sits
        // alongside Property.IsCurrentlyFeatured: two expressions of one rule, deliberately
        // adjacent so drift between them is visible. ForRentAndSale prefers PurchasePrice (a
        // listing offered both ways is still, at heart, being sold) and falls back to
        // whichever rent field is set.
        IQueryable<decimal?> prices = listingType switch
        {
            ListingType.ForSale => query.Select(p => p.PurchasePrice),
            ListingType.ForRent => query.Select(p => p.ColdRent ?? p.WarmRent),
            _ => query.Select(p => p.PurchasePrice ?? p.ColdRent ?? p.WarmRent)
        };

        return await prices
            .Where(price => price != null && price > 0)
            .Select(price => price!.Value)
            .ToListAsync(ct);
    }

    /// <summary>
    /// The Valuation Fast Path's matching rules (Phase 3), extracted as a public/static
    /// IQueryable-in-IQueryable-out method — same reasoning as ApplyFilter/ApplySort above:
    /// it runs unchanged whether the source is EF Core's <c>_db.Properties</c> (real query,
    /// translated to SQL) or an in-memory <c>List&lt;Property&gt;.AsQueryable()</c> (LINQ-to-
    /// Objects), which is what lets the matching rules themselves be unit-tested with no
    /// database — see GetComparableListingsMatchingTests.
    ///
    /// Eligibility mirrors ApplyFilter's own "published and not expired" — fuller than
    /// FindPotentialDuplicatesAsync's IsPublished-only check, and what actually matches what
    /// shows up in real search results. GovernorateId is always applied, even alongside a
    /// more specific district/neighborhood match, as a defence against a Property row whose
    /// location columns don't actually agree (nothing currently enforces that they do).
    /// District/Neighborhood are mutually exclusive: Neighborhood only when given, District
    /// only when Neighborhood is absent — never both, and never a silent fallback from
    /// Neighborhood to District just because Neighborhood is set.
    /// </summary>
    public static IQueryable<Property> ApplyComparableListingsFilter(
        IQueryable<Property> query,
        int? propertyTypeId,
        ListingType listingType,
        decimal? area,
        decimal areaTolerancePercent,
        int governorateId,
        int? districtId,
        int? neighborhoodId,
        DateTime nowUtc)
    {
        query = query.Where(p =>
            p.ListingType == listingType &&
            p.IsPublished &&
            (p.ExpiresAt == null || p.ExpiresAt > nowUtc) &&
            p.GovernorateId == governorateId);

        if (propertyTypeId.HasValue)
            query = query.Where(p => p.PropertyTypeId == propertyTypeId.Value);

        if (neighborhoodId.HasValue)
            query = query.Where(p => p.NeighborhoodId == neighborhoodId.Value);
        else if (districtId.HasValue)
            query = query.Where(p => p.DistrictId == districtId.Value);

        if (area is { } a && a > 0)
        {
            var minArea = a * (1 - areaTolerancePercent / 100m);
            var maxArea = a * (1 + areaTolerancePercent / 100m);
            query = query.Where(p => p.Area != null && p.Area >= minArea && p.Area <= maxArea);
        }

        return query;
    }

    /// <summary>
    /// In-memory twin of the price expression inside GetComparableListingPricesAsync — same
    /// rule, expressed as a plain C# method instead of a SQL-translatable expression, so it
    /// can run against ordinary in-memory Property objects in a unit test. Same mapping
    /// CheckPotentialDuplicatePropertyQueryHandler.GetComparablePrice uses for ForSale/ForRent.
    /// </summary>
    public static decimal? ResolveComparablePrice(Property property, ListingType listingType) =>
        listingType switch
        {
            ListingType.ForSale => property.PurchasePrice,
            ListingType.ForRent => property.ColdRent ?? property.WarmRent,
            _ => property.PurchasePrice ?? property.ColdRent ?? property.WarmRent
        };

    public void Update(Property property)
    {
        _db.Properties.Update(property);
    }

    public void Remove(Property property)
    {
        // Soft delete is handled in AppDbContext.SaveChangesAsync()
        // Calling Remove() here will be intercepted and converted to IsDeleted=true
        _db.Properties.Remove(property);
    }

    /// <summary>Names of the selected governorate/district, used to match legacy free-text locations.</summary>
    public sealed record LocationTextFallback(
        string? GovernorateNameAr, string? GovernorateNameEn,
        string? DistrictNameAr, string? DistrictNameEn);

    /// <summary>
    /// Looks up the selected governorate/district names so <see cref="ApplyFilter"/> can also match
    /// legacy listings that have no structured location ids. Returns null when no structured
    /// location filter is set (no extra query).
    /// </summary>
    public static async Task<LocationTextFallback?> ResolveLocationFallbackAsync(
        AppDbContext db, PropertyFilterDto filter, CancellationToken ct = default)
    {
        if (!filter.GovernorateId.HasValue && !filter.DistrictId.HasValue)
            return null;

        string? gAr = null, gEn = null, dAr = null, dEn = null;

        if (filter.GovernorateId is { } gid)
        {
            var g = await db.Governorates.AsNoTracking()
                .Where(x => x.Id == gid).Select(x => new { x.NameAr, x.NameEn })
                .FirstOrDefaultAsync(ct);
            gAr = g?.NameAr; gEn = g?.NameEn;
        }

        if (filter.DistrictId is { } did)
        {
            var d = await db.Districts.AsNoTracking()
                .Where(x => x.Id == did).Select(x => new { x.NameAr, x.NameEn })
                .FirstOrDefaultAsync(ct);
            dAr = d?.NameAr; dEn = d?.NameEn;
        }

        return new LocationTextFallback(gAr, gEn, dAr, dEn);
    }

    /// <summary>
    /// Applies all PropertyFilterDto filter clauses to an IQueryable&lt;Property&gt;.
    /// Extracted out of GetPagedAsync (Phase-0 tech-debt cleanup) so the
    /// SavedSearchMatchHostedService can reuse the exact same search rules
    /// instead of re-implementing (and inevitably drifting from) them.
    /// Public/static and side-effect free — safe to call from other repositories/services.
    /// </summary>
    public static IQueryable<Property> ApplyFilter(
        IQueryable<Property> query,
        PropertyFilterDto filter,
        LocationTextFallback? locationFallback = null)
    {
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            query = query.Where(p =>
                EF.Functions.ILike(p.Title, $"%{term}%") ||
                EF.Functions.ILike(p.Description, $"%{term}%"));
        }

        if (!string.IsNullOrWhiteSpace(filter.CountryCode))
            query = query.Where(p => p.CountryCode == filter.CountryCode.ToUpperInvariant());

        if (!string.IsNullOrWhiteSpace(filter.City))
            query = query.Where(p => EF.Functions.ILike(p.City, $"%{filter.City.Trim()}%"));

        if (!string.IsNullOrWhiteSpace(filter.Region))
            query = query.Where(p => EF.Functions.ILike(p.Region!, $"%{filter.Region.Trim()}%"));

        if (!string.IsNullOrWhiteSpace(filter.CurrencyCode))
            query = query.Where(p => p.CurrencyCode == filter.CurrencyCode.ToUpperInvariant());

        // Structured location filters take precedence in matching accuracy over the
        // free-text City/Region filters above — callers may pass both, in which case
        // both are applied (AND'ed) since a client could combine a governorate filter
        // with an unrelated free-text refinement.
        //
        // Legacy listings (created before the structured location columns existed) carry NULL
        // GovernorateId/DistrictId and hold the place only as free text in City/Region/DistrictText.
        // When the caller resolved the selected lookup's names (see ResolveLocationFallbackAsync),
        // a NULL-id row still matches by exact (case-insensitive) name; a row that does have an id
        // is matched strictly by id, so a stale text value can never override a real id.
        if (filter.GovernorateId.HasValue)
        {
            var gid = filter.GovernorateId.Value;
            if (locationFallback?.GovernorateNameAr is { } gAr && locationFallback.GovernorateNameEn is { } gEn)
                query = query.Where(p => p.GovernorateId == gid ||
                    (p.GovernorateId == null &&
                     (EF.Functions.ILike(p.City, gAr) || EF.Functions.ILike(p.City, gEn))));
            else
                query = query.Where(p => p.GovernorateId == gid);
        }

        if (filter.DistrictId.HasValue)
        {
            var did = filter.DistrictId.Value;
            if (locationFallback?.DistrictNameAr is { } dAr && locationFallback.DistrictNameEn is { } dEn)
                query = query.Where(p => p.DistrictId == did ||
                    (p.DistrictId == null &&
                     (EF.Functions.ILike(p.Region!, dAr) || EF.Functions.ILike(p.Region!, dEn) ||
                      EF.Functions.ILike(p.DistrictText!, dAr) || EF.Functions.ILike(p.DistrictText!, dEn))));
            else
                query = query.Where(p => p.DistrictId == did);
        }

        if (filter.NeighborhoodId.HasValue)
            query = query.Where(p => p.NeighborhoodId == filter.NeighborhoodId.Value);

        if (filter.PropertyTypeId.HasValue)
            query = query.Where(p => p.PropertyTypeId == filter.PropertyTypeId.Value);

        if (filter.ListingType.HasValue)
            query = query.Where(p => p.ListingType == filter.ListingType.Value);

        if (filter.Status.HasValue)
            query = query.Where(p => p.Status == filter.Status.Value);

        if (filter.Condition.HasValue)
            query = query.Where(p => p.Condition == filter.Condition.Value);

        if (filter.MinPrice.HasValue)
        {
            query = filter.ListingType switch
            {
                ListingType.ForRent =>
                    query.Where(p => p.ColdRent >= filter.MinPrice || p.WarmRent >= filter.MinPrice),
                ListingType.ForSale =>
                    query.Where(p => p.PurchasePrice >= filter.MinPrice),
                _ => // ForRentAndSale أو غير محدد
                    query.Where(p =>
                        (p.ColdRent >= filter.MinPrice || p.WarmRent >= filter.MinPrice) ||
                        p.PurchasePrice >= filter.MinPrice)
            };
        }

        if (filter.MaxPrice.HasValue)
        {
            query = filter.ListingType switch
            {
                ListingType.ForRent =>
                    query.Where(p =>
                        (p.ColdRent == null || p.ColdRent <= filter.MaxPrice) &&
                        (p.WarmRent == null || p.WarmRent <= filter.MaxPrice)),
                ListingType.ForSale =>
                    query.Where(p => p.PurchasePrice == null || p.PurchasePrice <= filter.MaxPrice),
                _ =>
                    query.Where(p =>
                        (p.PurchasePrice == null || p.PurchasePrice <= filter.MaxPrice) &&
                        (p.ColdRent == null || p.ColdRent <= filter.MaxPrice) &&
                        (p.WarmRent == null || p.WarmRent <= filter.MaxPrice))
            };
        }

        if (filter.MinRooms.HasValue)
            query = query.Where(p => p.Rooms >= filter.MinRooms);

        if (filter.MaxRooms.HasValue)
            query = query.Where(p => p.Rooms <= filter.MaxRooms);

        if (filter.MinArea.HasValue)
            query = query.Where(p => p.Area >= filter.MinArea);

        if (filter.MaxArea.HasValue)
            query = query.Where(p => p.Area <= filter.MaxArea);

        if (filter.HasBalcony.HasValue)
            query = query.Where(p => p.HasBalcony == filter.HasBalcony);

        if (filter.HasElevator.HasValue)
            query = query.Where(p => p.HasElevator == filter.HasElevator);

        if (filter.HasParkingSpace.HasValue)
            query = query.Where(p => p.HasParkingSpace == filter.HasParkingSpace);

        if (filter.OwnerId.HasValue)
            query = query.Where(p => p.OwnerId == filter.OwnerId);

        if (filter.AgencyId.HasValue)
            query = query.Where(p => p.AgencyId == filter.AgencyId);

        if (filter.AmenityIds is { Count: > 0 })
            query = query.Where(p =>
                filter.AmenityIds.All(aid =>
                    p.PropertyAmenities.Any(pa => pa.AmenityId == aid)));

        // Only published listings by default
        var now = DateTime.UtcNow;

        query = query.Where(property =>
            property.IsPublished &&
            (property.ExpiresAt == null ||
             property.ExpiresAt > now));

        return query;
    }

    /// <summary>
    /// Applies the result ordering, including paid featured placement.
    ///
    /// Featured listings are promoted in the DEFAULT ordering ONLY. When the caller asked for
    /// an explicit ordering — cheapest first, largest area — that ordering is honoured exactly
    /// as asked: a paid placement must never make "cheapest first" untrue.
    ///
    /// nowUtc is a parameter rather than DateTime.UtcNow read inline, so the ordering is
    /// deterministic under test and identical across the count and page queries of one request.
    ///
    /// No index backs the featured key. It is a time-dependent expression, and a PostgreSQL
    /// expression index requires an IMMUTABLE function — now() is not one. Deliberate, not an
    /// oversight: the featured rows are a small minority and the filtered index on
    /// FeaturedUntil already serves the sweep.
    ///
    /// Public/static and side-effect free, mirroring ApplyFilter — that is what lets
    /// PropertySortOrderTests exercise the real rule with no database.
    /// </summary>
    public static IQueryable<Property> ApplySort(
        IQueryable<Property> query,
        PropertyFilterDto filter,
        DateTime nowUtc)
    {
        // Property.CurrentlyFeatured is the SQL-translatable twin of the domain's
        // IsCurrentlyFeatured — a listing whose paid window has already elapsed is not
        // promoted, even in the hours before the sweep clears its flag.
        var featuredFirst = Property.CurrentlyFeatured(nowUtc);

        return (filter.SortBy?.ToLowerInvariant(), filter.SortDescending) switch
        {
            ("purchaseprice", true) => query.OrderByDescending(p => p.PurchasePrice),
            ("purchaseprice", false) => query.OrderBy(p => p.PurchasePrice),
            ("coldrent", true) => query.OrderByDescending(p => p.ColdRent),
            ("coldrent", false) => query.OrderBy(p => p.ColdRent),
            ("area", true) => query.OrderByDescending(p => p.Area),
            ("area", false) => query.OrderBy(p => p.Area),

            // Newest-first is the default the vast majority of traffic sees, and the only
            // place a paid placement outranks the requested order.
            ("createdat", false) => query
                .OrderByDescending(featuredFirst)
                .ThenBy(p => p.CreatedAt),
            _ => query
                .OrderByDescending(featuredFirst)
                .ThenByDescending(p => p.CreatedAt)
        };
    }
}
