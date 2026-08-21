using MediatR;
using PropertyApi.Application.Search.DTOs;
using PropertyApi.Application.Search.Interfaces;
using PropertyApi.Domain.Search.Entities;

namespace PropertyApi.Application.Search.Queries.GetMySavedSearches;

public sealed class GetMySavedSearchesQueryHandler
    : IRequestHandler<GetMySavedSearchesQuery, IReadOnlyList<SavedSearchDto>>
{
    private readonly ISavedSearchRepository _repo;

    public GetMySavedSearchesQueryHandler(ISavedSearchRepository repo)
        => _repo = repo;

    public async Task<IReadOnlyList<SavedSearchDto>> Handle(
        GetMySavedSearchesQuery request,
        CancellationToken cancellationToken)
    {
        var savedSearches = await _repo.GetByUserIdAsync(request.UserId, cancellationToken);
        return savedSearches.Select(MapToDto).ToList();
    }

    internal static SavedSearchDto MapToDto(SavedSearch s) => new(
        s.Id,
        s.Name,
        s.CountryCode,
        s.City,
        s.Region,
        s.GovernorateId,
        s.DistrictId,
        s.NeighborhoodId,
        s.PropertyTypeId,
        s.ListingType,
        s.MinPrice,
        s.MaxPrice,
        s.CurrencyCode,
        s.MinRooms,
        s.MaxRooms,
        s.MinArea,
        s.MaxArea,
        s.CreatedAt,
        s.LastMatchedAt);
}
