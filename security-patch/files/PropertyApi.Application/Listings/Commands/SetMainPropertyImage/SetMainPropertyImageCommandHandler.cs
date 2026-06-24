using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;

namespace PropertyApi.Application.Listings.Commands.SetMainPropertyImage;

public sealed class SetMainPropertyImageCommandHandler
    : IRequestHandler<SetMainPropertyImageCommand, PropertyImageMutationResult>
{
    private readonly IPropertyImageRepository _images;
    private readonly IPropertyOwnershipService _ownership;
    private readonly IUnitOfWork _uow;

    public SetMainPropertyImageCommandHandler(
        IPropertyImageRepository images,
        IPropertyOwnershipService ownership,
        IUnitOfWork uow)
    {
        _images = images;
        _ownership = ownership;
        _uow = uow;
    }

    public async Task<PropertyImageMutationResult> Handle(
        SetMainPropertyImageCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _ownership.EnsureOwnerAsync(
                request.PropertyId,
                request.UserId,
                operation: "set main image for",
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

        var targetImage = property.Images.FirstOrDefault(image => image.Id == request.ImageId);
        if (targetImage is null)
            return PropertyImageMutationResult.NotFound();

        foreach (var image in property.Images)
            image.IsMain = image.Id == request.ImageId;

        await _uow.SaveChangesAsync(cancellationToken);
        return PropertyImageMutationResult.Success();
    }
}
