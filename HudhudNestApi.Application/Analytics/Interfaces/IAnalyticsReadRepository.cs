using HudhudNestApi.Application.Analytics.DTOs;

namespace HudhudNestApi.Application.Analytics.Interfaces;

public interface IAnalyticsReadRepository
{
    Task<PropertyStatsDto> GetPropertyStatsAsync(
        Guid propertyId,
        Guid ownerId,
        CancellationToken ct = default);

    Task<MarketInsightsDto> GetMarketInsightsAsync(
        string? countryCode,
        CancellationToken ct = default);
}

