using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.DTOs;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Infrastructure.Caching;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Lookups;

public sealed class CommonLookupService : ICommonLookupService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _db;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CommonLookupService> _logger;

    public CommonLookupService(
        AppDbContext db,
        IDistributedCache cache,
        ILogger<CommonLookupService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AmenityLookupDto>> GetAmenitiesAsync(
        CancellationToken ct = default)
    {
        var result = await _cache.GetOrCreateAsync(
            LookupCacheKeys.Amenities,
            CacheDuration,
            async token =>
            {
                _logger.LogInformation(
                    "Cache miss for lookup key {CacheKey}. Loading amenities from database.",
                    LookupCacheKeys.Amenities);

                var items = await _db.Amenities
                    .AsNoTracking()
                    .OrderBy(x => x.Category)
                    .ThenBy(x => x.Name)
                    .Select(x => new AmenityLookupDto(
                        x.Id.ToString(),
                        x.Name,
                        x.Category,
                        x.IconName))
                    .ToListAsync(token);

                return (IReadOnlyList<AmenityLookupDto>)items;
            },
            ct);

        return result ?? Array.Empty<AmenityLookupDto>();
    }

    public async Task<IReadOnlyList<LookupItemDto>> GetCategoriesAsync(
        CancellationToken ct = default)
    {
        var result = await _cache.GetOrCreateAsync(
            LookupCacheKeys.Categories,
            CacheDuration,
            token =>
            {
                _logger.LogInformation(
                    "Cache miss for lookup key {CacheKey}. Loading categories from static source.",
                    LookupCacheKeys.Categories);

                IReadOnlyList<LookupItemDto> items =
                [
                    new LookupItemDto("rent", "Rent"),
                    new LookupItemDto("sale", "Sale"),
                    new LookupItemDto("residential", "Residential"),
                    new LookupItemDto("commercial", "Commercial")
                ];

                return Task.FromResult(items);
            },
            ct);

        return result ?? Array.Empty<LookupItemDto>();
    }

    public async Task<IReadOnlyList<LookupItemDto>> GetPropertyTypesAsync(
        CancellationToken ct = default)
    {
        var result = await _cache.GetOrCreateAsync(
            LookupCacheKeys.PropertyTypes,
            CacheDuration,
            token =>
            {
                _logger.LogInformation(
                    "Cache miss for lookup key {CacheKey}. Loading property types from static source.",
                    LookupCacheKeys.PropertyTypes);

                IReadOnlyList<LookupItemDto> items =
                [
                    new LookupItemDto("apartment", "Apartment"),
                    new LookupItemDto("house", "House"),
                    new LookupItemDto("villa", "Villa"),
                    new LookupItemDto("office", "Office"),
                    new LookupItemDto("land", "Land")
                ];

                return Task.FromResult(items);
            },
            ct);

        return result ?? Array.Empty<LookupItemDto>();
    }

    public async Task<IReadOnlyList<LookupItemDto>> GetCitiesAsync(
        CancellationToken ct = default)
    {
        var result = await _cache.GetOrCreateAsync(
            LookupCacheKeys.Cities,
            CacheDuration,
            async token =>
            {
                _logger.LogInformation(
                    "Cache miss for lookup key {CacheKey}. Loading cities from properties.",
                    LookupCacheKeys.Cities);

                var items = await _db.Properties
                    .AsNoTracking()
                    .Where(x => x.City != null && x.City != "")
                    .Select(x => x.City!)
                    .Distinct()
                    .OrderBy(x => x)
                    .Select(city => new LookupItemDto(city, city))
                    .ToListAsync(token);

                return (IReadOnlyList<LookupItemDto>)items;
            },
            ct);

        return result ?? Array.Empty<LookupItemDto>();
    }

    public async Task<IReadOnlyList<StructuredLocationLookupDto>> GetGovernoratesAsync(
        string countryCode = "SY",
        CancellationToken ct = default)
    {
        // Fixed small set (14 Syrian governorates today) — same fixed-key
        // caching pattern as Categories/PropertyTypes above. countryCode is
        // part of the cache key so a future non-"SY" catalog doesn't collide.
        var normalizedCountryCode = countryCode.Trim().ToUpperInvariant();
        var cacheKey = $"{LookupCacheKeys.Governorates}:{normalizedCountryCode}";

        var result = await _cache.GetOrCreateAsync(
            cacheKey,
            CacheDuration,
            async token =>
            {
                _logger.LogInformation(
                    "Cache miss for lookup key {CacheKey}. Loading governorates from database.",
                    cacheKey);

                var items = await _db.Governorates
                    .AsNoTracking()
                    .Where(g => g.IsActive && g.CountryCode == normalizedCountryCode)
                    .OrderBy(g => g.SortOrder)
                    .ThenBy(g => g.NameEn)
                    .Select(g => new StructuredLocationLookupDto(g.Id, g.NameAr, g.NameEn, null))
                    .ToListAsync(token);

                return (IReadOnlyList<StructuredLocationLookupDto>)items;
            },
            ct);

        return result ?? Array.Empty<StructuredLocationLookupDto>();
    }

    public async Task<IReadOnlyList<StructuredLocationLookupDto>> GetDistrictsAsync(
        int governorateId,
        CancellationToken ct = default)
    {
        // Deliberately NOT cached the same way as the fixed lists above: this
        // result is scoped by governorateId, and IDistributedCache has no
        // "remove by prefix" — caching every parent id indefinitely would leak
        // stale entries with no way to invalidate them via RefreshAllAsync.
        // The underlying table is small and indexed on GovernorateId, so a
        // direct query per call is cheap enough without caching.
        return await _db.Districts
            .AsNoTracking()
            .Where(d => d.IsActive && d.GovernorateId == governorateId)
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.NameEn)
            .Select(d => new StructuredLocationLookupDto(d.Id, d.NameAr, d.NameEn, d.GovernorateId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StructuredLocationLookupDto>> GetNeighborhoodsAsync(
        int districtId,
        CancellationToken ct = default)
    {
        // Same reasoning as GetDistrictsAsync — parent-scoped, not cached.
        return await _db.Neighborhoods
            .AsNoTracking()
            .Where(n => n.IsActive && n.DistrictId == districtId)
            .OrderBy(n => n.SortOrder)
            .ThenBy(n => n.NameEn)
            .Select(n => new StructuredLocationLookupDto(n.Id, n.NameAr, n.NameEn, n.DistrictId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PropertyTypeLookupDto>> GetPropertyTypeCatalogAsync(
        CancellationToken ct = default)
    {
        var result = await _cache.GetOrCreateAsync(
            LookupCacheKeys.PropertyTypeCatalog,
            CacheDuration,
            async token =>
            {
                _logger.LogInformation(
                    "Cache miss for lookup key {CacheKey}. Loading property type catalog from database.",
                    LookupCacheKeys.PropertyTypeCatalog);

                var items = await _db.PropertyTypes
                    .AsNoTracking()
                    .Where(pt => pt.IsActive)
                    .OrderBy(pt => pt.SortOrder)
                    .ThenBy(pt => pt.NameEn)
                    .Select(pt => new PropertyTypeLookupDto(pt.Id, pt.Code, pt.NameAr, pt.NameEn, pt.Category, pt.Icon))
                    .ToListAsync(token);

                return (IReadOnlyList<PropertyTypeLookupDto>)items;
            },
            ct);

        return result ?? Array.Empty<PropertyTypeLookupDto>();
    }

    public async Task RefreshAsync(
        string cacheKey,
        CancellationToken ct = default)
    {
        var normalizedKey = NormalizeKey(cacheKey);

        _logger.LogInformation(
            "Removing lookup cache key {CacheKey}.",
            normalizedKey);

        await _cache.RemoveAsync(normalizedKey, ct);
    }

    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Removing all common lookup cache keys.");

        await _cache.RemoveAsync(LookupCacheKeys.Amenities, ct);
        await _cache.RemoveAsync(LookupCacheKeys.Categories, ct);
        await _cache.RemoveAsync(LookupCacheKeys.PropertyTypes, ct);
        await _cache.RemoveAsync(LookupCacheKeys.Cities, ct);
        await _cache.RemoveAsync($"{LookupCacheKeys.Governorates}:SY", ct);
        await _cache.RemoveAsync(LookupCacheKeys.PropertyTypeCatalog, ct);
        // Districts/Neighborhoods are not cached (see GetDistrictsAsync) — nothing to remove here.
    }

    private static string NormalizeKey(string cacheKey)
    {
        return cacheKey.Trim().ToLowerInvariant() switch
        {
            "amenities" => LookupCacheKeys.Amenities,
            "categories" => LookupCacheKeys.Categories,
            "property-types" => LookupCacheKeys.PropertyTypes,
            "propertytypes" => LookupCacheKeys.PropertyTypes,
            "cities" => LookupCacheKeys.Cities,
            "governorates" => $"{LookupCacheKeys.Governorates}:SY",
            "property-type-catalog" => LookupCacheKeys.PropertyTypeCatalog,
            "propertytypecatalog" => LookupCacheKeys.PropertyTypeCatalog,
            _ => throw new ArgumentException(
                $"Unsupported lookup cache key '{cacheKey}'.",
                nameof(cacheKey))
        };
    }
}