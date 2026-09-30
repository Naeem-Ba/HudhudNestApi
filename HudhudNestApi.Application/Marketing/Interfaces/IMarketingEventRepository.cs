using HudhudNestApi.Application.Marketing.DTOs;
using HudhudNestApi.Domain.Marketing.Entities;

namespace HudhudNestApi.Application.Marketing.Interfaces;

public interface IMarketingEventRepository
{
    void Add(MarketingEvent marketingEvent);

    Task<MarketingEventsSummaryDto> GetSummaryAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default);
}
