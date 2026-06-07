using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Controllers;

/// <summary>
/// إدارة العقارات المفضلة للمستخدم.
/// الآن يُضاف كـ feature مكتمل.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class FavoritesController : ControllerBase
{
    private readonly AppDbContext _db;

    public FavoritesController(AppDbContext db)
        => _db = db;

    // GET /api/favorites  (قائمة مفضلاتي)
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyFavorites(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var favorites = await _db.Favorites
            .Where(f => f.UserId == userId)
            .Include(f => f.Property)
                .ThenInclude(p => p!.Images.Where(i => i.IsMain))
            .Select(f => new
            {
                f.PropertyId,
                f.CreatedAt,
                Property = new
                {
                    f.Property!.Title,
                    f.Property.City,
                    f.Property.CountryCode,
                    f.Property.ColdRent,
                    f.Property.PurchasePrice,
                    MainImageUrl = f.Property.Images
                        .Where(i => i.IsMain)
                        .Select(i => i.Url)
                        .FirstOrDefault()
                }
            })
            .ToListAsync(ct);

        return Ok(favorites);
    }

    // POST /api/favorites/{propertyId}  (إضافة للمفضلة)
    [HttpPost("{propertyId:guid}")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Add(Guid propertyId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        // تحقق من عدم الإضافة مسبقاً
        var exists = await _db.Favorites
            .AnyAsync(f => f.UserId == userId && f.PropertyId == propertyId, ct);

        if (exists)
            return Conflict(new { message = "Property already in favorites." });

        _db.Favorites.Add(new Favorite
        {
            UserId = userId.Value,
            PropertyId = propertyId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        return StatusCode(StatusCodes.Status201Created,
            new { message = "Added to favorites." });
    }

    // DELETE /api/favorites/{propertyId}  (حذف من المفضلة)
    [HttpDelete("{propertyId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid propertyId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var fav = await _db.Favorites
            .FirstOrDefaultAsync(
                f => f.UserId == userId && f.PropertyId == propertyId, ct);

        if (fav is null) return NotFound();

        _db.Favorites.Remove(fav);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}