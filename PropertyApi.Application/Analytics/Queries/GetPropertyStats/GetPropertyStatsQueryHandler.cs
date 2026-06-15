using MediatR;
using PropertyApi.Application.Analytics.DTOs;
using PropertyApi.Application.Analytics.Interfaces;

namespace PropertyApi.Application.Analytics.Queries.GetPropertyStats;

public sealed class GetPropertyStatsQueryHandler
    : IRequestHandler<GetPropertyStatsQuery, PropertyStatsDto>
{
    private readonly IAnalyticsReadRepository _analytics;

    public GetPropertyStatsQueryHandler(IAnalyticsReadRepository analytics)
    {
        _analytics = analytics;
    }

    public Task<PropertyStatsDto> Handle(
        GetPropertyStatsQuery request,
        CancellationToken ct)
    {
        return _analytics.GetPropertyStatsAsync(
            request.PropertyId,
            request.OwnerId,
            ct);
    }
}

