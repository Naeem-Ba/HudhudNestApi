using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Reviews.Interfaces;
using HudhudNestApi.Domain.Bookings.Enums;
using HudhudNestApi.Domain.Reviews.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Reviews;

public sealed class AgencyRatingRepository : IAgencyRatingRepository
{
    private readonly AppDbContext _db;

    public AgencyRatingRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(AgencyRating rating, CancellationToken ct = default)
        => await _db.AgencyRatings.AddAsync(rating, ct);

    public async Task<AgencyRating?> GetByRaterAndAgencyAsync(
        Guid agencyId,
        Guid raterId,
        CancellationToken ct = default)
        => await _db.AgencyRatings.FirstOrDefaultAsync(
            r => r.AgencyId == agencyId && r.RaterId == raterId,
            ct);

    public async Task<AgencyRatingAverages> GetAveragesAsync(Guid agencyId, CancellationToken ct = default)
    {
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
                Overall = g.Average(r =>
                    (r.Service + r.Professionalism + r.Reliability + r.OverallExperience) / 4.0)
            })
            .FirstOrDefaultAsync(ct);

        return aggregate is null
            ? new AgencyRatingAverages(0, 0, 0, 0, 0, 0)
            : new AgencyRatingAverages(
                aggregate.Service,
                aggregate.Professionalism,
                aggregate.Reliability,
                aggregate.OverallExperience,
                aggregate.Overall,
                aggregate.Count);
    }

    public async Task<IReadOnlyDictionary<Guid, (double Overall, int Count)>> GetOverallSummariesAsync(
        IReadOnlyCollection<Guid> agencyIds,
        CancellationToken ct = default)
    {
        if (agencyIds.Count == 0)
            return new Dictionary<Guid, (double, int)>();

        var rows = await _db.AgencyRatings
            .AsNoTracking()
            .Where(r => agencyIds.Contains(r.AgencyId))
            .GroupBy(r => r.AgencyId)
            .Select(g => new
            {
                AgencyId = g.Key,
                Count = g.Count(),
                Overall = g.Average(r =>
                    (r.Service + r.Professionalism + r.Reliability + r.OverallExperience) / 4.0)
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.AgencyId, r => (r.Overall, r.Count));
    }

    public async Task<(IReadOnlyList<AgencyRating> Ratings, int TotalCount)> GetByAgencyAsync(
        Guid agencyId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.AgencyRatings
            .AsNoTracking()
            .Where(r => r.AgencyId == agencyId);

        var totalCount = await query.CountAsync(ct);
        var ratings = await query
            .Include(r => r.Rater)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (ratings.AsReadOnly(), totalCount);
    }

    public async Task<bool> HasInteractionWithAgencyAsync(
        Guid raterId,
        Guid agencyId,
        CancellationToken ct = default)
    {
        var visited = await _db.VisitRequests
            .AnyAsync(v =>
                v.RequesterId == raterId &&
                v.Status == VisitStatus.Completed &&
                v.Property!.AgencyId == agencyId,
                ct);

        if (visited)
            return true;

        return await _db.Messages
            .AnyAsync(m =>
                (m.SenderId == raterId || m.ReceiverId == raterId) &&
                m.Property!.AgencyId == agencyId,
                ct);
    }
}
