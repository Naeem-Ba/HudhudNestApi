using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/amenities")]
public sealed class AmenitiesController : ControllerBase
{
    private const string AmenitiesCacheKey = "amenities:all:v1";

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public AmenitiesController(
        AppDbContext db,
        IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    // GET /api/amenities
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var amenities = await _cache.GetOrCreateAsync(
            AmenitiesCacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                entry.SlidingExpiration = TimeSpan.FromMinutes(15);

                return await _db.Amenities
                    .AsNoTracking()
                    .OrderBy(amenity => amenity.Category)
                    .ThenBy(amenity => amenity.Name)
                    .Select(amenity => new AmenityDto
                    {
                        Id = amenity.Id,
                        Name = amenity.Name,
                        Category = amenity.Category,
                        IconName = amenity.IconName
                    })
                    .ToListAsync(ct);
            });

        return Ok(amenities ?? []);
    }
}

public sealed class AmenityDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Category { get; init; }

    public string? IconName { get; init; }
}