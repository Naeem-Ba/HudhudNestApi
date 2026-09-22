using MediatR;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Listings.Mappers;

namespace HudhudNestApi.Application.Listings.Queries.GetMyProperties;

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

        // One clock reading for the whole list, so two listings whose featured windows
        // straddle "now" are not judged against two different instants in one response.
        var asOfUtc = DateTime.UtcNow;

        return properties.Select(property => PropertyMapper.ToDto(property, asOfUtc)).ToList();
    }
}
