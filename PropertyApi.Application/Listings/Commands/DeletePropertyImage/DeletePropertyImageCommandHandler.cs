using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;

namespace PropertyApi.Application.Listings.Commands.DeletePropertyImage;

public sealed class DeletePropertyImageCommandHandler
    : IRequestHandler<DeletePropertyImageCommand, PropertyImageMutationResult>
{
    private readonly IPropertyImageRepository _images;
    private readonly IMediaStorageService _storage;
    private readonly IUnitOfWork _uow;

    public DeletePropertyImageCommandHandler(
        IPropertyImageRepository images,
        IMediaStorageService storage,
        IUnitOfWork uow)
    {
        _images = images;
        _storage = storage;
        _uow = uow;
    }

    public async Task<PropertyImageMutationResult> Handle(
        DeletePropertyImageCommand request,
        CancellationToken cancellationToken)
    {
        var property = await _images.GetPropertyWithImagesAsync(request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyImageMutationResult.NotFound();

        if (property.OwnerId != request.UserId)
            return PropertyImageMutationResult.Forbidden();

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

