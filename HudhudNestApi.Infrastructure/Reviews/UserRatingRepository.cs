using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Reviews.Interfaces;
using HudhudNestApi.Domain.Reviews.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Reviews;

public sealed class UserRatingRepository : IUserRatingRepository
{
    private readonly AppDbContext _db;

    public UserRatingRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(UserRating rating, CancellationToken ct = default)
        => await _db.UserRatings.AddAsync(rating, ct);

    public void Update(UserRating rating)
        => _db.UserRatings.Update(rating);

    public async Task<UserRating?> GetByRaterAndRatedAsync(
        Guid ratedUserId,
        Guid raterId,
        CancellationToken ct = default)
        => await _db.UserRatings.FirstOrDefaultAsync(
            r => r.RatedUserId == ratedUserId && r.RaterId == raterId,
            ct);

    public async Task<bool> HasRatedAsync(
        Guid ratedUserId,
        Guid raterId,
        CancellationToken ct = default)
        => await _db.UserRatings.AnyAsync(
            r => r.RatedUserId == ratedUserId && r.RaterId == raterId,
            ct);

    public async Task<UserRatingAverages> GetAveragesAsync(
        Guid ratedUserId,
        CancellationToken ct = default)
    {
        var query = _db.UserRatings
            .AsNoTracking()
            .Where(r => r.RatedUserId == ratedUserId);

        // Single round-trip, aggregated in the database from the stored scores
        // (previously every row was loaded into memory first). Average() over
        // a nullable column skips nulls, so the optional criteria only average
        // the ratings that actually scored them. Overall is the mean of each
        // rating's own score, using the same formula as UserRating.OverallScore.
        var aggregate = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Credibility = g.Average(r => (double)r.Credibility),
                Safety = g.Average(r => (double)r.Safety),
                ResponseSpeed = g.Average(r => (double)r.ResponseSpeed),
                Transparency = g.Average(r => (double)r.Transparency),
                InformationAccuracy = g.Average(r => (double?)r.InformationAccuracy),
                Conduct = g.Average(r => (double?)r.Conduct),
                Overall = g.Average(r =>
                    (r.Credibility + r.Safety + r.ResponseSpeed + r.Transparency
                        + (r.InformationAccuracy ?? 0) + (r.Conduct ?? 0))
                    / (4.0
                        + (r.InformationAccuracy != null ? 1 : 0)
                        + (r.Conduct != null ? 1 : 0)))
            })
            .FirstOrDefaultAsync(ct);

        if (aggregate is null || aggregate.Count == 0)
        {
            return new UserRatingAverages(0, 0, 0, 0, null, null, 0, 0);
        }

        return new UserRatingAverages(
            aggregate.Credibility,
            aggregate.Safety,
            aggregate.ResponseSpeed,
            aggregate.Transparency,
            aggregate.InformationAccuracy,
            aggregate.Conduct,
            aggregate.Overall,
            aggregate.Count);
    }

    public async Task<(IReadOnlyList<UserRating> Ratings, int TotalCount)> GetByRatedUserIdAsync(
        Guid ratedUserId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.UserRatings
            .AsNoTracking()
            .Where(r => r.RatedUserId == ratedUserId);

        var totalCount = await query.CountAsync(ct);

        var ratings = await query
            .Include(r => r.Rater)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (ratings.AsReadOnly(), totalCount);
    }
}
