using MediatR;
using HudhudNestApi.Application.Amenities.DTOs;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Application.Amenities.Queries.GetAmenities;

public sealed class GetAmenitiesQueryHandler
    : IRequestHandler<GetAmenitiesQuery, IReadOnlyList<AmenityDto>>
{
    private readonly ICommonLookupService _lookups;

    public GetAmenitiesQueryHandler(ICommonLookupService lookups)
        => _lookups = lookups;

    public async Task<IReadOnlyList<AmenityDto>> Handle(
        GetAmenitiesQuery request,
        CancellationToken cancellationToken)
    {
        var amenities = await _lookups.GetAmenitiesAsync(cancellationToken);

        return amenities
            .Select(amenity => new AmenityDto
            {
                Id = Guid.TryParse(amenity.Id, out var id) ? id : Guid.Empty,
                Name = amenity.Name,
                Category = amenity.Category,
                IconName = amenity.IconName
            })
            .ToList();
    }
}

