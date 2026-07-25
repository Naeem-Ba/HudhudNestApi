using MediatR;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Listings.Mappers;

namespace PropertyApi.Application.Listings.Queries.GetMyProperties;

public sealed class GetMyPropertiesQueryHandler
    : IRequestHandler<GetMyPropertiesQuery, IReadOnlyList<PropertyDto>>
{
    private readonly IPropertyRepository _repository;

    public GetMyPropertiesQueryHandler(IPropertyRepository repository)
        => _repository = repository;

    public async Task<IReadOnlyList<PropertyDto>> Handle(
        GetMyPropertiesQuery request,
        CancellationToken cancellationToken)
    {
        var properties = await _repository.GetByOwnerAsync(
            request.OwnerId,
            cancellationToken);

        return properties.Select(PropertyMapper.ToDto).ToList();
    }
}
