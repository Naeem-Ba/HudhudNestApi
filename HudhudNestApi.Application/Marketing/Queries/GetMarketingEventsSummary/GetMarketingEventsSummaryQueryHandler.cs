using MediatR;
using HudhudNestApi.Application.Marketing.DTOs;
using HudhudNestApi.Application.Marketing.Interfaces;

namespace HudhudNestApi.Application.Marketing.Queries.GetMarketingEventsSummary;

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
