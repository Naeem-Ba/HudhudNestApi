using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Search.Interfaces;
using PropertyApi.Domain.Search.Entities;

namespace PropertyApi.Application.Search.Commands.CreateSavedSearch;

public sealed class CreateSavedSearchCommandHandler
    : IRequestHandler<CreateSavedSearchCommand, Guid>
{
    private readonly ISavedSearchRepository _repo;
    private readonly IUnitOfWork _uow;

    public CreateSavedSearchCommandHandler(ISavedSearchRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<Guid> Handle(CreateSavedSearchCommand request, CancellationToken cancellationToken)
    {
        var savedSearch = SavedSearch.Create(
            userId: request.UserId,
            name: request.Name,
            countryCode: request.CountryCode,
            city: request.City,
            region: request.Region,
            governorateId: request.GovernorateId,
            districtId: request.DistrictId,
            neighborhoodId: request.NeighborhoodId,
            propertyTypeId: request.PropertyTypeId,
            listingType: request.ListingType,
            minPrice: request.MinPrice,
            maxPrice: request.MaxPrice,
            currencyCode: request.CurrencyCode,
            minRooms: request.MinRooms,
            maxRooms: request.MaxRooms,
            minArea: request.MinArea,
            maxArea: request.MaxArea);

        _repo.Add(savedSearch);
        await _uow.SaveChangesAsync(cancellationToken);

        return savedSearch.Id;
    }
}
