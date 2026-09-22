using MediatR;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;

namespace HudhudNestApi.Application.ShortStay.Queries.GetActiveAccommodationTypes;

/// <summary>
/// Public catalog read for the "type of place" picker (create-listing form) and the search
/// filter's accommodation-type dropdown. IAccommodationTypeRepository.GetActiveAsync already
/// existed (used internally by CreateShortStayListingCommandHandler to validate the id) but
/// nothing ever exposed the list itself — a host had no way to discover which
/// AccommodationTypeId values are even valid before this.
/// </summary>
public sealed record GetActiveAccommodationTypesQuery : IRequest<IReadOnlyList<AccommodationTypeDto>>;

public sealed class GetActiveAccommodationTypesQueryHandler
    : IRequestHandler<GetActiveAccommodationTypesQuery, IReadOnlyList<AccommodationTypeDto>>
{
    private readonly IAccommodationTypeRepository _accommodationTypes;

    public GetActiveAccommodationTypesQueryHandler(IAccommodationTypeRepository accommodationTypes)
        => _accommodationTypes = accommodationTypes;

    public async Task<IReadOnlyList<AccommodationTypeDto>> Handle(
        GetActiveAccommodationTypesQuery request, CancellationToken ct)
    {
        var types = await _accommodationTypes.GetActiveAsync(ct);

        return types
            .OrderBy(t => t.SortOrder)
            .Select(t => new AccommodationTypeDto(t.Id, t.Code, t.NameAr, t.NameEn, t.Category, t.Icon, t.SortOrder))
            .ToList();
    }
}
