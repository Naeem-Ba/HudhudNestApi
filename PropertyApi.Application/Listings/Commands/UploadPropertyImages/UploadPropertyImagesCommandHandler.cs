using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Listings.Commands.UploadPropertyImages;

public sealed class UploadPropertyImagesCommandHandler
    : IRequestHandler<UploadPropertyImagesCommand, UploadPropertyImagesResult>
{
    private const long MaxImageSize = 5_000_000;
    private const string ImageFolder = "property-images";

    private static readonly HashSet<string> AllowedImageTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

    private readonly IPropertyImageRepository _images;
    private readonly IMediaStorageService _storage;
    private readonly IUnitOfWork _uow;

    public UploadPropertyImagesCommandHandler(
        IPropertyImageRepository images,
        IMediaStorageService storage,
        IUnitOfWork uow)
    {
        _images = images;
        _storage = storage;
        _uow = uow;
    }

    public async Task<UploadPropertyImagesResult> Handle(
        UploadPropertyImagesCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Files.Count == 0)
            return UploadPropertyImagesResult.ValidationFailed("No files uploaded.");

        var property = await _images.GetPropertyByIdAsync(request.PropertyId, cancellationToken);
        if (property is null)
            return UploadPropertyImagesResult.NotFound();

        if (property.OwnerId != request.UserId)
            return UploadPropertyImagesResult.Forbidden();

        var existingImageCount = await _images.CountImagesAsync(request.PropertyId, cancellationToken);
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
}
