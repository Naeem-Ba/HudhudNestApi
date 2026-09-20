using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Application.ShortStay.Commands.AddShortStayListingPhotos;

public sealed class AddShortStayListingPhotosCommandHandler
    : IRequestHandler<AddShortStayListingPhotosCommand, IReadOnlyList<string>>
{
    private const long MaxImageSize = 5_000_000;
    private const int MaxFilesPerUpload = 10;
    private const int MaxPhotosPerListing = 20;
    private const string PhotoFolder = "short-stay-photos";

    private static readonly HashSet<string> AllowedImageTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    private static readonly HashSet<string> AllowedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    private readonly IShortStayListingRepository _listings;
    private readonly IMediaStorageService _storage;
    private readonly IUnitOfWork _uow;

    public AddShortStayListingPhotosCommandHandler(
        IShortStayListingRepository listings, IMediaStorageService storage, IUnitOfWork uow)
    {
        _listings = listings;
        _storage = storage;
        _uow = uow;
    }

    public async Task<IReadOnlyList<string>> Handle(AddShortStayListingPhotosCommand request, CancellationToken ct)
    {
        if (request.Files.Count == 0)
            throw new DomainException("لم يتم رفع أي ملف.");

        if (request.Files.Count > MaxFilesPerUpload)
            throw new DomainException($"يمكن رفع {MaxFilesPerUpload} ملفات كحد أقصى في كل مرة.");

        var listing = await _listings.GetByIdWithDetailsAsync(request.ListingId, ct)
            ?? throw new NotFoundException($"Listing {request.ListingId} was not found.");

        if (listing.OwnerId != request.OwnerId)
            throw new ForbiddenException("Only the listing owner can upload photos.");

        if (listing.Photos.Count + request.Files.Count > MaxPhotosPerListing)
            throw new DomainException($"لا يمكن أن يتجاوز عدد الصور {MaxPhotosPerListing} صورة لكل إعلان.");

        foreach (var file in request.Files)
        {
            var error = await ValidateAsync(file, ct);
            if (error is not null) throw new DomainException(error);
        }

        var uploadedPublicIds = new List<string>();
        var uploadedUrls = new List<string>();

        try
        {
            foreach (var file in request.Files)
            {
                if (file.Content.CanSeek) file.Content.Position = 0;
                await using var content = file.Content;

                var result = await _storage.UploadImageAsync(content, file.FileName, file.ContentType, PhotoFolder, ct);
                // Already-uploaded files are removed once, by the catch below.
                if (!result.Succeeded)
                    throw new DomainException(result.ErrorMessage ?? "تعذر رفع الصورة.");

                uploadedPublicIds.Add(result.PublicId!);

                listing.Photos.Add(new ShortStayListingPhoto
                {
                    Url = result.Url!,
                    PublicId = result.PublicId!,
                    IsMain = listing.Photos.Count == 0,
                    SortOrder = listing.Photos.Count,
                    ShortStayListingId = listing.Id
                });

                uploadedUrls.Add(result.Url!);
            }

            await _uow.SaveChangesAsync(ct);
            return uploadedUrls;
        }
        catch
        {
            await CleanupAsync(uploadedPublicIds, CancellationToken.None);
            throw;
        }
    }

    private async Task CleanupAsync(IEnumerable<string> publicIds, CancellationToken ct)
    {
        foreach (var publicId in publicIds)
        {
            if (!string.IsNullOrWhiteSpace(publicId))
                await _storage.DeleteImageAsync(publicId, ct);
        }
    }

    private static async Task<string?> ValidateAsync(UploadPropertyImageFileDto file, CancellationToken ct)
    {
        if (file.Length == 0) return $"الملف '{file.FileName}' فارغ.";
        if (file.Length > MaxImageSize) return $"الملف '{file.FileName}' أكبر من 5 ميجابايت.";
        if (!AllowedImageTypes.Contains(file.ContentType)) return $"الملف '{file.FileName}' ليس صورة مدعومة.";
        if (!AllowedImageExtensions.Contains(Path.GetExtension(file.FileName)))
            return $"امتداد الملف '{file.FileName}' غير مدعوم.";
        if (!await HasValidImageSignatureAsync(file.Content, file.ContentType, ct))
            return $"الملف '{file.FileName}' ليس ملف صورة صالحاً.";
        return null;
    }

    private static async Task<bool> HasValidImageSignatureAsync(Stream content, string contentType, CancellationToken ct)
    {
        if (!content.CanSeek) return false;

        var originalPosition = content.Position;
        var header = new byte[12];
        var bytesRead = await content.ReadAsync(header.AsMemory(0, header.Length), ct);
        content.Position = originalPosition;

        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            "image/png" => bytesRead >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E
                && header[3] == 0x47 && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
            "image/webp" => bytesRead >= 12 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46
                && header[3] == 0x46 && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50,
            _ => false
        };
    }
}
