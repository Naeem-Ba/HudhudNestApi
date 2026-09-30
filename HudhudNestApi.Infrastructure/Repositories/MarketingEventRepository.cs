using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Marketing.DTOs;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Marketing.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories;

public sealed class MarketingEventRepository : IMarketingEventRepository
{
    private readonly AppDbContext _db;

    public MarketingEventRepository(AppDbContext db)
        => _db = db;

    public void Add(MarketingEvent marketingEvent)
    {
        _db.MarketingEvents.Add(marketingEvent);
    }

    public async Task<MarketingEventsSummaryDto> GetSummaryAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        var counts = await _db.MarketingEvents
            .AsNoTracking()
            .Where(e => e.CreatedAt >= fromUtc && e.CreatedAt <= toUtc)
            .GroupBy(e => e.EventType)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new MarketingEventsSummaryDto(
            fromUtc,
            toUtc,
            counts.ToDictionary(x => x.Key.ToString(), x => x.Count));
    }
}
