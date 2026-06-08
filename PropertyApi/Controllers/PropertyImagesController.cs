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
/// تحسينات الأمان:
///   1. لا يعتمد فقط على ContentType لأنه قابل للتزوير.
///   2. يتحقق من امتداد الملف.
///   3. يتحقق من Magic Bytes للملف.
///   4. يتحقق من الحجم قبل الرفع.
///   5. يحذف الملفات المرفوعة من Cloudinary إذا فشل جزء من العملية.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/images")]
[Authorize]
public sealed class PropertyImagesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CloudinaryMediaStorageService _storage;

    private const long MaxImageSize = 5_000_000;
    private const int HeaderSize = 12;

    private static readonly HashSet<string> AllowedImageTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

    private static readonly HashSet<string> AllowedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

    private static readonly byte[] JpegMagicBytes =
        { 0xFF, 0xD8, 0xFF };

    private static readonly byte[] PngMagicBytes =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

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
    [RequestSizeLimit(20_000_000)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Upload(
        Guid propertyId,
        [FromForm] IFormFileCollection files,
        CancellationToken ct)
    {
        if (files is null || files.Count == 0)
            return BadRequest(new { message = "No files uploaded." });

        var property = await _db.Properties
            .Include(property => property.Images)
            .FirstOrDefaultAsync(property => property.Id == propertyId, ct);

        if (property is null)
            return NotFound();

        var userId = GetCurrentUserId();

        if (userId is null)
            return Unauthorized();

        if (property.OwnerId != userId)
            return Forbid();

        foreach (var file in files)
        {
            var validationError = await ValidateImageFileAsync(file, ct);

            if (validationError is not null)
                return BadRequest(new { message = validationError });
        }

        var uploadedPublicIds = new List<string>();
        var uploaded = new List<ImageResultDto>();

        try
        {
            foreach (var file in files)
            {
                var result = await _storage.UploadAsync(
                    file,
                    folder: "property-images",
                    ct);

                if (result.Error is not null)
                {
                    await CleanupUploadedImagesAsync(uploadedPublicIds, ct);

                    return BadRequest(new
                    {
                        message = result.Error.Message
                    });
                }

                uploadedPublicIds.Add(result.PublicId);

                var isMain = !property.Images.Any();

                var image = new PropertyImage
                {
                    Url = result.SecureUrl.AbsoluteUri,
                    PublicId = result.PublicId,
                    IsMain = isMain,
                    SortOrder = property.Images.Count,
                    PropertyId = propertyId
                };

                property.Images.Add(image);

                uploaded.Add(new ImageResultDto(
                    image.Id,
                    image.Url,
                    image.IsMain,
                    image.SortOrder));
            }

            await _db.SaveChangesAsync(ct);

            return Ok(uploaded);
        }
        catch
        {
            await CleanupUploadedImagesAsync(uploadedPublicIds, ct);
            throw;
        }
    }

    // ------------------------------------------------------------------
    // GET /api/properties/{propertyId}/images
    // قراءة كل صور عقار منشور
    // ------------------------------------------------------------------
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
    // تعيين صورة كصورة رئيسية
    // ------------------------------------------------------------------
    [HttpPatch("{imageId:guid}/setmain")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetMain(
        Guid propertyId,
        Guid imageId,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
            return Unauthorized();

        var property = await _db.Properties
            .Include(property => property.Images)
            .FirstOrDefaultAsync(property => property.Id == propertyId, ct);

        if (property is null)
            return NotFound();

        if (property.OwnerId != userId)
            return Forbid();

        var targetImage = property.Images
            .FirstOrDefault(image => image.Id == imageId);

        if (targetImage is null)
            return NotFound();

        foreach (var image in property.Images)
            image.IsMain = image.Id == imageId;

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ------------------------------------------------------------------
    // DELETE /api/properties/{propertyId}/images/{imageId}
    // حذف صورة من Cloudinary أولاً ثم من قاعدة البيانات
    // ------------------------------------------------------------------
    [HttpDelete("{imageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Delete(
        Guid propertyId,
        Guid imageId,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
            return Unauthorized();

        var property = await _db.Properties
            .Include(property => property.Images)
            .FirstOrDefaultAsync(property => property.Id == propertyId, ct);

        if (property is null)
            return NotFound();

        if (property.OwnerId != userId)
            return Forbid();

        var imageToDelete = property.Images
            .FirstOrDefault(image => image.Id == imageId);

        if (imageToDelete is null)
            return NotFound();

        var wasMain = imageToDelete.IsMain;
        var publicId = imageToDelete.PublicId;

        // 1. Delete from external storage first.
        // If this fails, keep the DB unchanged so we do not lose the only reference
        // to a still-public external image.
        if (!string.IsNullOrWhiteSpace(publicId))
        {
            var deletionResult = await _storage.DeleteAsync(publicId, ct);

            if (deletionResult.Error is not null)
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new
                    {
                        message = "Storage deletion failed. The image was not deleted from the database.",
                        storageError = deletionResult.Error.Message
                    });
            }
        }

        // 2. Delete from database only after storage deletion succeeds.
        _db.PropertyImages.Remove(imageToDelete);

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

        return NoContent();
    }

    // ------------------------------------------------------------------
    // Validation helpers
    // ------------------------------------------------------------------
    private static async Task<string?> ValidateImageFileAsync(
        IFormFile file,
        CancellationToken ct)
    {
        if (file.Length == 0)
            return $"File '{file.FileName}' is empty.";

        if (file.Length > MaxImageSize)
            return $"File '{file.FileName}' is larger than 5 MB.";

        if (!AllowedImageTypes.Contains(file.ContentType))
            return $"File '{file.FileName}' has an unsupported content type.";

        if (!HasAllowedExtension(file))
            return $"File '{file.FileName}' has an unsupported extension.";

        var hasValidMagicBytes = await HasValidImageMagicBytesAsync(file, ct);

        if (!hasValidMagicBytes)
            return $"File '{file.FileName}' is not a valid image file.";

        return null;
    }

    private static bool HasAllowedExtension(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);

        return !string.IsNullOrWhiteSpace(extension) &&
               AllowedImageExtensions.Contains(extension);
    }

    private static async Task<bool> HasValidImageMagicBytesAsync(
        IFormFile file,
        CancellationToken ct)
    {
        var headerBytes = new byte[HeaderSize];

        await using var stream = file.OpenReadStream();

        var bytesRead = await stream.ReadAsync(
            headerBytes.AsMemory(0, headerBytes.Length),
            ct);

        return IsJpeg(headerBytes, bytesRead) ||
               IsPng(headerBytes, bytesRead) ||
               IsWebp(headerBytes, bytesRead);
    }

    private static bool IsJpeg(byte[] headerBytes, int bytesRead)
    {
        return StartsWith(headerBytes, bytesRead, JpegMagicBytes);
    }

    private static bool IsPng(byte[] headerBytes, int bytesRead)
    {
        return StartsWith(headerBytes, bytesRead, PngMagicBytes);
    }

    private static bool IsWebp(byte[] headerBytes, int bytesRead)
    {
        return bytesRead >= 12 &&
               headerBytes[0] == 0x52 && // R
               headerBytes[1] == 0x49 && // I
               headerBytes[2] == 0x46 && // F
               headerBytes[3] == 0x46 && // F
               headerBytes[8] == 0x57 && // W
               headerBytes[9] == 0x45 && // E
               headerBytes[10] == 0x42 && // B
               headerBytes[11] == 0x50;  // P
    }

    private static bool StartsWith(
        byte[] headerBytes,
        int bytesRead,
        byte[] magicBytes)
    {
        return bytesRead >= magicBytes.Length &&
               headerBytes
                   .AsSpan(0, magicBytes.Length)
                   .SequenceEqual(magicBytes);
    }

    private async Task CleanupUploadedImagesAsync(
        IEnumerable<string> publicIds,
        CancellationToken ct)
    {
        foreach (var publicId in publicIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
        {
            try
            {
                await _storage.DeleteAsync(publicId, ct);
            }
            catch
            {
                // Cleanup failure should not hide the original upload/database error.
            }
        }
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(claim, out var id)
            ? id
            : null;
    }
}

public sealed record ImageResultDto(
    Guid Id,
    string Url,
    bool IsMain,
    int SortOrder);