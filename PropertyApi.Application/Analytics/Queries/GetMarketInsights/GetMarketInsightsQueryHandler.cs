using MediatR;
using PropertyApi.Application.Analytics.DTOs;
using PropertyApi.Application.Analytics.Interfaces;

namespace PropertyApi.Application.Analytics.Queries.GetMarketInsights;

public sealed class GetMarketInsightsQueryHandler
    : IRequestHandler<GetMarketInsightsQuery, MarketInsightsDto>
{
    private readonly IAnalyticsReadRepository _analytics;

    public GetMarketInsightsQueryHandler(IAnalyticsReadRepository analytics)
    {
        _analytics = analytics;
    }

    public Task<MarketInsightsDto> Handle(
        GetMarketInsightsQuery request,
        CancellationToken ct)
    {
        return _analytics.GetMarketInsightsAsync(request.CountryCode, ct);
    }
}

