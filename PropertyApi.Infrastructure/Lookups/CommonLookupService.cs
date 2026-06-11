using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.DTOs;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Caching;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Lookups;

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
            _ => throw new ArgumentException(
                $"Unsupported lookup cache key '{cacheKey}'.",
                nameof(cacheKey))
        };
    }
}