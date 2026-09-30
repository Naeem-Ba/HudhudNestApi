using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Search.Interfaces;
using HudhudNestApi.Domain.Search.Entities;

namespace HudhudNestApi.Application.Search.Commands.CreateSavedSearch;

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
