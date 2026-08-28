using MediatR;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Application.Services.Mapping;
using PropertyApi.Domain.Services.Enums;

namespace PropertyApi.Application.Services.Queries.GetActiveOfferingsByCategory;

public sealed record GetActiveOfferingsByCategoryQuery(
    ServiceCategory Category) : IRequest<IReadOnlyList<ServiceOfferingDto>>;

public sealed class GetActiveOfferingsByCategoryQueryHandler
    : IRequestHandler<GetActiveOfferingsByCategoryQuery, IReadOnlyList<ServiceOfferingDto>>
{
    private readonly IServiceOfferingRepository _offerings;

    public GetActiveOfferingsByCategoryQueryHandler(IServiceOfferingRepository offerings)
        => _offerings = offerings;

    public async Task<IReadOnlyList<ServiceOfferingDto>> Handle(
        GetActiveOfferingsByCategoryQuery request, CancellationToken ct)
    {
        var offerings = await _offerings.GetActiveByCategoryAsync(request.Category, ct);
        return offerings.Select(ServiceMapper.ToDto).ToList().AsReadOnly();
    }
}
