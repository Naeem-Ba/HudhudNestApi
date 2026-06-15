using PropertyApi.Application.Analytics.DTOs;

namespace PropertyApi.Application.Analytics.Interfaces;

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

