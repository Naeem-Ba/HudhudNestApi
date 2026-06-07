using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Infrastructure.Media;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Controllers;

/// <summary>
/// إدارة صور العقارات.
/// تحسينات على النسخة القديمة:
///   1. يستخدم CloudinaryMediaStorageService (Infrastructure) بدل PhotoService (API layer)
///   2. Guid IDs بدل int IDs
///   3. SortOrder و AltText للأولوية وإمكانية الوصول
///   4. DELETE يحذف الصورة من Cloudinary أيضاً
///   5. PATCH /setmain لتغيير الصورة الرئيسية
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/images")]
[Authorize]
public sealed class PropertyImagesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CloudinaryMediaStorageService _storage;
    private const long MaxImageSize = 5_000_000;

    private static readonly HashSet<string> AllowedImageTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "image/jpeg",
        "image/png",
        "image/webp"
        };

    public PropertyImagesController(
        AppDbContext db,
        CloudinaryMediaStorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    // ------------------------------------------------------------------
    // POST /api/properties/{propertyId}/images
    // رفع صورة واحدة أو أكثر لعقار
    // ------------------------------------------------------------------
    [HttpPost]
    [RequestSizeLimit(20_000_000)]  // 20 MB
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Upload(
        Guid propertyId,
        [FromForm] IFormFileCollection files,
        CancellationToken ct)
    {
        if (files is null || files.Count == 0)
            return BadRequest(new { message = "No files uploaded." });

        // التحقق من وجود العقار وأن الطالب هو المالك
        var property = await _db.Properties
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == propertyId, ct);

        if (property is null) return NotFound();

        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        if (property.OwnerId != userId)
            return Forbid();

        var uploadedPublicIds = new List<string>();
        var uploaded = new List<ImageResultDto>();

        foreach (var file in files)
        {
            if (file.Length == 0)
                continue;

            if (file.Length > MaxImageSize)
            {
                return BadRequest(new
                {
                    message = $"File '{file.FileName}' is larger than 5 MB."
                });
            }

            if (!AllowedImageTypes.Contains(file.ContentType))
            {
                return BadRequest(new
                {
                    message = $"File '{file.FileName}' is not a supported image."
                });
            }

            var result = await _storage.UploadAsync(
                file, folder: "property-images", ct);

            if (result.Error is not null)
                return BadRequest(new { message = result.Error.Message });

            // أول صورة تُرفع لعقار بدون صور = صورة رئيسية
            var isMain = !property.Images.Any();

            var img = new PropertyImage
            {
                Url = result.SecureUrl.AbsoluteUri,
                PublicId = result.PublicId,
                IsMain = isMain,
                SortOrder = property.Images.Count,
                PropertyId = propertyId
            };

            property.Images.Add(img);
            uploaded.Add(new ImageResultDto(
                img.Id, img.Url, img.IsMain, img.SortOrder));
        }

        await _db.SaveChangesAsync(ct);
        return Ok(uploaded);
    }

    // ------------------------------------------------------------------
    // GET /api/properties/{propertyId}/images
    // قراءة كل صور عقار
    // ------------------------------------------------------------------
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
    Guid propertyId,
    CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var propertyIsPublic = await _db.Properties
            .AsNoTracking()
            .AnyAsync(
                property =>
                    property.Id == propertyId &&
                    property.IsPublished &&
                    (property.ExpiresAt == null ||
                     property.ExpiresAt > now),
                ct);

        if (!propertyIsPublic)
            return NotFound();

        var images = await _db.PropertyImages
            .AsNoTracking()
            .Where(image => image.PropertyId == propertyId)
            .OrderBy(image => image.SortOrder)
            .Select(image => new ImageResultDto(
                image.Id,
                image.Url,
                image.IsMain,
                image.SortOrder))
            .ToListAsync(ct);

        return Ok(images);
    }

    // ------------------------------------------------------------------
    // PATCH /api/properties/{propertyId}/images/{imageId}/setmain
    // تعيين صورة كـ "رئيسية"
    // ------------------------------------------------------------------
    [HttpPatch("{imageId:guid}/setmain")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetMain(
        Guid propertyId, Guid imageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var property = await _db.Properties
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == propertyId, ct);

        if (property is null) return NotFound();
        if (property.OwnerId != userId) return Forbid();

        var targetImage = property.Images
    .FirstOrDefault(image => image.Id == imageId);

        if (targetImage is null)
            return NotFound();

        // أزل الـ "رئيسية" من كل الصور
        foreach (var image in property.Images)
            image.IsMain = image.Id == imageId;

        await _db.SaveChangesAsync(ct);
        return NoContent();


    }

    // ------------------------------------------------------------------
    // DELETE /api/properties/{propertyId}/images/{imageId}
    // حذف صورة من Cloudinary ومن قاعدة البيانات
    // ------------------------------------------------------------------
    [HttpDelete("{imageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
    Guid propertyId, Guid imageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var property = await _db.Properties
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == propertyId, ct);

        if (property is null) return NotFound();
        if (property.OwnerId != userId) return Forbid();

        var img = property.Images.FirstOrDefault(i => i.Id == imageId);
        if (img is null) return NotFound();

        // ✅ احفظ حالة IsMain قبل الحذف
        var wasMain = img.IsMain;

        // ✅ احذف من قاعدة البيانات أولاً
        _db.PropertyImages.Remove(img);

        // ✅ إذا كانت الصورة المحذوفة هي الرئيسية، عيّن البديل
        if (wasMain)
        {
            var replacement = property.Images
                .Where(image => image.Id != imageId)
                .OrderBy(image => image.SortOrder)
                .FirstOrDefault();

            if (replacement is not null)
                replacement.IsMain = true;
        }

        await _db.SaveChangesAsync(ct);

        // ✅ احذف من Cloudinary بعد نجاح حفظ قاعدة البيانات
        if (!string.IsNullOrWhiteSpace(img.PublicId))
            await _storage.DeleteAsync(img.PublicId, ct);

        return NoContent();
    }

    // ── Helper ──────────────────────────────────────────────────────────
    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────
public sealed record ImageResultDto(
    Guid Id,
    string Url,
    bool IsMain,
    int SortOrder
);