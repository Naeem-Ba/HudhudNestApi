using MediatR;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Queries.GetActiveOfferingsByCategory;

/// <summary>
/// Public catalog. Only bookable offerings (see IServiceOfferingRepository.GetActiveByCategoryAsync):
/// an inactive offering, a suspended or revoked provider, or a category no longer approved for the
/// provider never appears here. <paramref name="GovernorateId"/> narrows to providers covering it.
/// </summary>
public sealed record GetActiveOfferingsByCategoryQuery(
    ServiceCategory Category, int? GovernorateId = null) : IRequest<IReadOnlyList<ServiceOfferingDto>>;

public sealed class GetActiveOfferingsByCategoryQueryHandler
    : IRequestHandler<GetActiveOfferingsByCategoryQuery, IReadOnlyList<ServiceOfferingDto>>
{
    private readonly IServiceOfferingRepository _offerings;
    private readonly IServiceReviewRepository _reviews;

    public GetActiveOfferingsByCategoryQueryHandler(IServiceOfferingRepository offerings, IServiceReviewRepository reviews)
    {
        _offerings = offerings;
        _reviews = reviews;
    }

    public async Task<IReadOnlyList<ServiceOfferingDto>> Handle(
        GetActiveOfferingsByCategoryQuery request, CancellationToken ct)
    {
        var offerings = await _offerings.GetActiveByCategoryAsync(request.Category, request.GovernorateId, ct);
        return await offerings.ToDtosWithRatingsAsync(_reviews, ct);
    }
}

/// <summary>Bookable offering count for every category (zero included), for the discovery views.</summary>
public sealed record GetServiceCategoryAvailabilityQuery(int? GovernorateId = null)
    : IRequest<IReadOnlyList<ProviderOnboarding.ServiceCategoryAvailabilityDto>>;

public sealed class GetServiceCategoryAvailabilityQueryHandler
    : IRequestHandler<GetServiceCategoryAvailabilityQuery, IReadOnlyList<ProviderOnboarding.ServiceCategoryAvailabilityDto>>
{
    private readonly IServiceOfferingRepository _offerings;

    public GetServiceCategoryAvailabilityQueryHandler(IServiceOfferingRepository offerings)
        => _offerings = offerings;

    public async Task<IReadOnlyList<ProviderOnboarding.ServiceCategoryAvailabilityDto>> Handle(
        GetServiceCategoryAvailabilityQuery request, CancellationToken ct)
    {
        var counts = await _offerings.CountActiveByCategoryAsync(request.GovernorateId, ct);
        return Enum.GetValues<ServiceCategory>()
            .Select(c => new ProviderOnboarding.ServiceCategoryAvailabilityDto(c, counts.TryGetValue(c, out var n) ? n : 0))
            .ToList();
    }
}

/// <summary>The calling provider's own offerings, active or not.</summary>
public sealed record GetMyServiceOfferingsQuery(Guid ActorUserId) : IRequest<IReadOnlyList<ServiceOfferingDto>>;

public sealed class GetMyServiceOfferingsQueryHandler
    : IRequestHandler<GetMyServiceOfferingsQuery, IReadOnlyList<ServiceOfferingDto>>
{
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceOfferingRepository _offerings;
    private readonly IServiceReviewRepository _reviews;

    public GetMyServiceOfferingsQueryHandler(
        IServiceProviderRepository providers, IServiceOfferingRepository offerings, IServiceReviewRepository reviews)
    {
        _providers = providers;
        _offerings = offerings;
        _reviews = reviews;
    }

    public async Task<IReadOnlyList<ServiceOfferingDto>> Handle(GetMyServiceOfferingsQuery request, CancellationToken ct)
    {
        var provider = await _providers.GetByUserIdAsync(request.ActorUserId, ct);
        if (provider is null)
            return [];

        var offerings = await _offerings.GetByProviderIdAsync(provider.Id, ct);
        return await offerings.ToDtosWithRatingsAsync(_reviews, ct);
    }
}
