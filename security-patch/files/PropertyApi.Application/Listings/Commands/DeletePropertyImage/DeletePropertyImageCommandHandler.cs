using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;

namespace PropertyApi.Application.Listings.Commands.DeletePropertyImage;

public sealed class DeletePropertyImageCommandHandler
    : IRequestHandler<DeletePropertyImageCommand, PropertyImageMutationResult>
{
    private readonly IPropertyImageRepository _images;
    private readonly IPropertyOwnershipService _ownership;
    private readonly IMediaStorageService _storage;
    private readonly IUnitOfWork _uow;

    public DeletePropertyImageCommandHandler(
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

    public async Task<PropertyImageMutationResult> Handle(
        DeletePropertyImageCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _ownership.EnsureOwnerAsync(
                request.PropertyId,
                request.UserId,
                operation: "delete images from",
                ct: cancellationToken);
        }
        catch (NotFoundException)
        {
            return PropertyImageMutationResult.NotFound();
        }
        catch (ForbiddenException)
        {
            return PropertyImageMutationResult.Forbidden();
        }

        var property = await _images.GetPropertyWithImagesAsync(request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyImageMutationResult.NotFound();

        var image = property.Images.FirstOrDefault(i => i.Id == request.ImageId);
        if (image is null)
            return PropertyImageMutationResult.NotFound();

        var wasMain = image.IsMain;
        var publicId = image.PublicId;

        _images.Remove(image);

        if (wasMain)
        {
            var replacement = property.Images
                .Where(candidate => candidate.Id != request.ImageId)
                .OrderBy(candidate => candidate.SortOrder)
                .FirstOrDefault();

            if (replacement is not null)
                replacement.IsMain = true;
        }

        await _uow.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(publicId))
            await _storage.DeleteImageAsync(publicId, cancellationToken);

        return PropertyImageMutationResult.Success();
    }
}
