using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;

namespace PropertyApi.Application.Listings.Commands.SetMainPropertyImage;

public sealed class SetMainPropertyImageCommandHandler
    : IRequestHandler<SetMainPropertyImageCommand, PropertyImageMutationResult>
{
    private readonly IPropertyImageRepository _images;
    private readonly IUnitOfWork _uow;

    public SetMainPropertyImageCommandHandler(IPropertyImageRepository images, IUnitOfWork uow)
    {
        _images = images;
        _uow = uow;
    }

    public async Task<PropertyImageMutationResult> Handle(
        SetMainPropertyImageCommand request,
        CancellationToken cancellationToken)
    {
        var property = await _images.GetPropertyWithImagesAsync(request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyImageMutationResult.NotFound();

        if (property.OwnerId != request.UserId)
            return PropertyImageMutationResult.Forbidden();

        var targetImage = property.Images.FirstOrDefault(image => image.Id == request.ImageId);
        if (targetImage is null)
            return PropertyImageMutationResult.NotFound();

        foreach (var image in property.Images)
            image.IsMain = image.Id == request.ImageId;

        await _uow.SaveChangesAsync(cancellationToken);
        return PropertyImageMutationResult.Success();
    }
}

