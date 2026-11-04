using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Paging;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Bookings.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Agencies;

public sealed class AgencyRatingRepository : IAgencyRatingRepository
{
    private readonly AppDbContext _db;

    public AgencyRatingRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(AgencyRating rating, CancellationToken ct = default)
        => await _db.AgencyRatings.AddAsync(rating, ct);

    public Task<AgencyRating?> GetByRaterAndAgencyAsync(Guid agencyId, Guid raterId, CancellationToken ct = default)
        => _db.AgencyRatings.FirstOrDefaultAsync(r => r.AgencyId == agencyId && r.RaterId == raterId, ct);

    public async Task<AgencyRatingAverages> GetAveragesAsync(Guid agencyId, CancellationToken ct = default)
    {
        // One round-trip, aggregated in the database from the stored scores.
        var aggregate = await _db.AgencyRatings
            .AsNoTracking()
            .Where(r => r.AgencyId == agencyId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Service = g.Average(r => (double)r.Service),
                Professionalism = g.Average(r => (double)r.Professionalism),
                Reliability = g.Average(r => (double)r.Reliability),
                OverallExperience = g.Average(r => (double)r.OverallExperience),
                Overall = g.Average(r => (r.Service + r.Professionalism + r.Reliability + r.OverallExperience) / 4.0)
            })
            .FirstOrDefaultAsync(ct);

        return aggregate is null || aggregate.Count == 0
            ? new AgencyRatingAverages(0, 0, 0, 0, 0, 0)
            : new AgencyRatingAverages(
                aggregate.Service, aggregate.Professionalism, aggregate.Reliability,
                aggregate.OverallExperience, aggregate.Overall, aggregate.Count);
    }

    public async Task<(IReadOnlyList<AgencyRating> Ratings, int TotalCount)> GetPageAsync(
        Guid agencyId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.AgencyRatings.AsNoTracking().Where(r => r.AgencyId == agencyId);
        var total = await query.CountAsync(ct);

        var rows = await query
            .Include(r => r.Rater)
            .OrderByDescending(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .SkipPage(page, pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (rows.AsReadOnly(), total);
    }

    public async Task<bool> HasInteractionAsync(Guid agencyId, Guid raterId, CancellationToken ct = default)
    {
        // Listings attributed to the office, including ones since deleted: a visit or a conversation about
        // a listing that is gone is still a real interaction with the office.
        var officeListings = _db.Properties
            .IgnoreQueryFilters()
            .Where(p => p.AgencyId == agencyId)
            .Select(p => p.Id);

        var visited = await _db.VisitRequests.AnyAsync(v =>
            v.RequesterId == raterId &&
            v.Status == VisitStatus.Completed &&
            officeListings.Contains(v.PropertyId), ct);

        if (visited)
            return true;

        return await _db.Messages.AnyAsync(m =>
            (m.SenderId == raterId || m.ReceiverId == raterId) &&
            officeListings.Contains(m.PropertyId), ct);
    }
}
