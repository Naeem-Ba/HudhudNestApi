using MediatR;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Listings.Mappers;
using HudhudNestApi.Application.Listings.Views;

namespace HudhudNestApi.Application.Listings.Queries.GetPropertyById;

/// <summary>
/// Public listing detail. Only published, unexpired listings are visible here — the owner's
/// own preview of an unpublished listing goes through GetPropertyForManagementQuery instead.
///
/// This handler used to carry its own internal MapToDto(), which GetPropertiesListQueryHandler
/// also called. That copy fell seven fields behind PropertyMapper.ToDto (structured location
/// and availability freshness), so the two public read paths quietly returned less than the
/// owner-facing ones. It is gone; there is one mapper.
/// </summary>
public sealed class GetPropertyByIdQueryHandler
    : IRequestHandler<GetPropertyByIdQuery, PropertyDto?>
{
    private readonly IPropertyRepository _repo;
    private readonly IListingViewRepository _views;

    public GetPropertyByIdQueryHandler(IPropertyRepository repo, IListingViewRepository views)
    {
        _repo = repo;
        _views = views;
    }

    public async Task<PropertyDto?> Handle(
        GetPropertyByIdQuery request,
        CancellationToken cancellationToken)
    {
        var property = await _repo.GetPublishedByIdWithDetailsAsync(request.Id, cancellationToken);
        if (property is null)
            return null;

        var dto = PropertyMapper.ToDto(property);
        await _views.ApplyViewCountsAsync(new[] { dto }, cancellationToken);
        return dto;
    }
}
