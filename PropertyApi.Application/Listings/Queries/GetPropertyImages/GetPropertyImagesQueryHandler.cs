using MediatR;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;

namespace PropertyApi.Application.Listings.Queries.GetPropertyImages;

public sealed class GetPropertyImagesQueryHandler
    : IRequestHandler<GetPropertyImagesQuery, PropertyImagesQueryResult>
{
    private readonly IPropertyImageRepository _images;

    public GetPropertyImagesQueryHandler(IPropertyImageRepository images)
        => _images = images;

    public async Task<PropertyImagesQueryResult> Handle(
        GetPropertyImagesQuery request,
        CancellationToken cancellationToken)
    {
        var propertyIsPublic = await _images.PublicPropertyExistsAsync(
            request.PropertyId,
            DateTime.UtcNow,
            cancellationToken);

        if (!propertyIsPublic)
            return PropertyImagesQueryResult.NotFound();

        var images = await _images.GetImagesAsync(request.PropertyId, cancellationToken);
        return PropertyImagesQueryResult.Success(images);
    }
}
