using MediatR;
using HudhudNestApi.Application.Analytics.DTOs;
using HudhudNestApi.Application.Analytics.Interfaces;

namespace HudhudNestApi.Application.Analytics.Queries.GetPropertyStats;

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

