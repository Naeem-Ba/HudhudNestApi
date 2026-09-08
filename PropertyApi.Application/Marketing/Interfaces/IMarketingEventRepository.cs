using PropertyApi.Application.Marketing.DTOs;
using PropertyApi.Domain.Marketing.Entities;

namespace PropertyApi.Application.Marketing.Interfaces;

public interface IMarketingEventRepository
{
    void Add(MarketingEvent marketingEvent);

    Task<MarketingEventsSummaryDto> GetSummaryAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default);
}
