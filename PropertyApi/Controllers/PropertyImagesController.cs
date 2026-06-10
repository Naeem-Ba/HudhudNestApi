using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Controllers;

/// <summary>
/// إدارة صور العقارات.
/// تحسينات على النسخة القديمة:
///   1. يعتمد على IMediaStorageService بدل Cloudinary concrete type
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
    private readonly IMediaStorageService _storage;
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
        IMediaStorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    // ------------------------------------------------------------------
    // POST /api/properties/{propertyId}/images
    // رفع صورة واحدة أو أكثر لعقار
    // ------------------------------------------------------------------
    [HttpPost]
[RequestSizeLimit(20_000_000)] // 20 MB
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

    var userId = GetCurrentUserId();
    if (userId is null)
        return Unauthorized();

    var property = await _db.Properties
        .AsNoTracking()
        .FirstOrDefaultAsync(p => p.Id == propertyId, ct);

    if (property is null)
        return NotFound();

    if (property.OwnerId != userId)
        return Forbid();

    var existingImageCount = await _db.PropertyImages
        .CountAsync(image => image.PropertyId == propertyId, ct);

    var uploaded = new List<ImageResultDto>();
    var uploadedPublicIds = new List<string>();

    try
    {
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

            await using var stream = file.OpenReadStream();

            var result = await _storage.UploadImageAsync(
                stream,
                file.FileName,
                file.ContentType,
                folder: "property-images",
                cancellationToken: ct);

            if (!result.Succeeded)
            {
                return BadRequest(new { message = result.ErrorMessage });
            }

            uploadedPublicIds.Add(result.PublicId!);

            var img = new PropertyImage
            {
                Url = result.Url!,
                PublicId = result.PublicId!,
                IsMain = existingImageCount == 0 && uploaded.Count == 0,
                SortOrder = existingImageCount + uploaded.Count,
                PropertyId = propertyId
            };

            _db.PropertyImages.Add(img);

            uploaded.Add(new ImageResultDto(
                img.Id,
                img.Url,
                img.IsMain,
                img.SortOrder));
        }

        await _db.SaveChangesAsync(ct);

        return Ok(uploaded);
    }
    catch
    {
        foreach (var publicId in uploadedPublicIds)
        {
            if (!string.IsNullOrWhiteSpace(publicId))
                await _storage.DeleteImageAsync(publicId, ct);
        }

        throw;
    }
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
            await _storage.DeleteImageAsync(img.PublicId, ct);

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