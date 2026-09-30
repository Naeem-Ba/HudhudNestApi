using MediatR;
using HudhudNestApi.Application.Analytics.DTOs;
using HudhudNestApi.Application.Analytics.Interfaces;

namespace HudhudNestApi.Application.Analytics.Queries.GetMarketInsights;

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

