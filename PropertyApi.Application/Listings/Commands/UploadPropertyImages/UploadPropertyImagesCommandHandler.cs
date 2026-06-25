using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Listings.Commands.UploadPropertyImages;

public sealed class UploadPropertyImagesCommandHandler
    : IRequestHandler<UploadPropertyImagesCommand, UploadPropertyImagesResult>
{
    private const long MaxImageSize = 5_000_000;
    private const int MaxImagesPerUpload = 10;
    private const int MaxImagesPerProperty = 20;
    private const string ImageFolder = "property-images";

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

    private readonly IPropertyImageRepository _images;
    private readonly IPropertyOwnershipService _ownership;
    private readonly IMediaStorageService _storage;
    private readonly IUnitOfWork _uow;

    public UploadPropertyImagesCommandHandler(
        IPropertyImageRepository images,
        IPropertyOwnershipService ownership,
        IMediaStorageService storage,
        IUnitOfWork uow)
    {
        _images = images;
        _ownership = ownership;
        _storage = storage;
        _uow = uow;
    }

    public async Task<UploadPropertyImagesResult> Handle(
        UploadPropertyImagesCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Files.Count == 0)
            return UploadPropertyImagesResult.ValidationFailed("No files uploaded.");

        if (request.Files.Count > MaxImagesPerUpload)
            return UploadPropertyImagesResult.ValidationFailed($"Upload at most {MaxImagesPerUpload} files per request.");

        try
        {
            await _ownership.EnsureOwnerAsync(
                request.PropertyId,
                request.UserId,
                operation: "upload images to",
                ct: cancellationToken);
        }
        catch (NotFoundException)
        {
            return UploadPropertyImagesResult.NotFound();
        }
        catch (ForbiddenException)
        {
            return UploadPropertyImagesResult.Forbidden();
        }

        var existingImageCount = await _images.CountImagesAsync(request.PropertyId, cancellationToken);
        if (existingImageCount + request.Files.Count > MaxImagesPerProperty)
        {
            return UploadPropertyImagesResult.ValidationFailed(
                $"A property can have at most {MaxImagesPerProperty} images.");
        }

        var uploaded = new List<PropertyImageDto>();
        var uploadedPublicIds = new List<string>();

        try
        {
            foreach (var file in request.Files)
            {
                await using var content = file.Content;

                if (file.Length == 0)
                    continue;

                if (file.Length > MaxImageSize)
                    return UploadPropertyImagesResult.ValidationFailed($"File '{file.FileName}' is larger than 5 MB.");

                if (!AllowedImageTypes.Contains(file.ContentType))
                    return UploadPropertyImagesResult.ValidationFailed($"File '{file.FileName}' is not a supported image.");

                if (!AllowedImageExtensions.Contains(Path.GetExtension(file.FileName)))
                    return UploadPropertyImagesResult.ValidationFailed($"File '{file.FileName}' has an unsupported extension.");

                if (!await HasValidImageSignatureAsync(file.Content, file.ContentType, cancellationToken))
                    return UploadPropertyImagesResult.ValidationFailed($"File '{file.FileName}' is not a valid image file.");

                var result = await _storage.UploadImageAsync(
                    content,
                    file.FileName,
                    file.ContentType,
                    ImageFolder,
                    cancellationToken);

                if (!result.Succeeded)
                    return UploadPropertyImagesResult.StorageFailed(result.ErrorMessage);

                uploadedPublicIds.Add(result.PublicId!);

                var image = new PropertyImage
                {
                    Url = result.Url!,
                    PublicId = result.PublicId!,
                    IsMain = existingImageCount == 0 && uploaded.Count == 0,
                    SortOrder = existingImageCount + uploaded.Count,
                    PropertyId = request.PropertyId
                };

                _images.Add(image);

                uploaded.Add(new PropertyImageDto(
                    image.Id,
                    image.Url,
                    image.IsMain,
                    image.SortOrder));
            }

            await _uow.SaveChangesAsync(cancellationToken);

            return UploadPropertyImagesResult.Success(uploaded);
        }
        catch
        {
            foreach (var publicId in uploadedPublicIds)
            {
                if (!string.IsNullOrWhiteSpace(publicId))
                    await _storage.DeleteImageAsync(publicId, cancellationToken);
            }

            throw;
        }
    }

    private static async Task<bool> HasValidImageSignatureAsync(
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (!content.CanSeek)
            return false;

        var originalPosition = content.Position;
        var header = new byte[12];
        var bytesRead = await content.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        content.Position = originalPosition;

        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => bytesRead >= 3 &&
                header[0] == 0xFF &&
                header[1] == 0xD8 &&
                header[2] == 0xFF,

            "image/png" => bytesRead >= 8 &&
                header[0] == 0x89 &&
                header[1] == 0x50 &&
                header[2] == 0x4E &&
                header[3] == 0x47 &&
                header[4] == 0x0D &&
                header[5] == 0x0A &&
                header[6] == 0x1A &&
                header[7] == 0x0A,

            "image/webp" => bytesRead >= 12 &&
                header[0] == 0x52 &&
                header[1] == 0x49 &&
                header[2] == 0x46 &&
                header[3] == 0x46 &&
                header[8] == 0x57 &&
                header[9] == 0x45 &&
                header[10] == 0x42 &&
                header[11] == 0x50,

            _ => false
        };
    }
}
