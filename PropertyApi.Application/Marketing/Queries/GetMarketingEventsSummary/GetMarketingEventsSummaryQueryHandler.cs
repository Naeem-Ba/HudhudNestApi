using MediatR;
using PropertyApi.Application.Marketing.DTOs;
using PropertyApi.Application.Marketing.Interfaces;

namespace PropertyApi.Application.Marketing.Queries.GetMarketingEventsSummary;

public sealed class GetMarketingEventsSummaryQueryHandler
    : IRequestHandler<GetMarketingEventsSummaryQuery, MarketingEventsSummaryDto>
{
    private readonly IMarketingEventRepository _events;

    public GetMarketingEventsSummaryQueryHandler(IMarketingEventRepository events)
        => _events = events;

    public Task<MarketingEventsSummaryDto> Handle(
        GetMarketingEventsSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var toUtc = request.ToUtc ?? DateTime.UtcNow;
        var fromUtc = request.FromUtc ?? toUtc.AddDays(-30);

        return _events.GetSummaryAsync(fromUtc, toUtc, cancellationToken);
    }
}
