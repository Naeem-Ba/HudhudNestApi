using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Application.Services.Mapping;

/// <summary>Maps offerings together with their visible rating, in one extra query for the whole list.</summary>
public static class ServiceOfferingMapping
{
    public static async Task<IReadOnlyList<ServiceOfferingDto>> ToDtosWithRatingsAsync(
        this IReadOnlyCollection<ServiceOffering> offerings,
        IServiceReviewRepository reviews,
        CancellationToken ct)
    {
        var ratings = await reviews.GetOfferingRatingSummariesAsync(
            offerings.Select(o => o.Id).ToList(), ct);

        return offerings
            .Select(o => ServiceMapper.ToDto(o, ratings.TryGetValue(o.Id, out var r) ? r : null))
            .ToList();
    }
}
