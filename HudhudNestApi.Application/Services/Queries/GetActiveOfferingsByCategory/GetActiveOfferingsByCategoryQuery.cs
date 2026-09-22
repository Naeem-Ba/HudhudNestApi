using MediatR;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Queries.GetActiveOfferingsByCategory;

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
