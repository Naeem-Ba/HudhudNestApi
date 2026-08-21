using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Reviews.Interfaces;
using PropertyApi.Domain.Reviews.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Reviews;

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

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
        {
            return new UserRatingAverages(0, 0, 0, 0, 0, 0);
        }

        // Single round-trip: aggregate all four criteria averages together
        // rather than four separate AverageAsync() queries.
        var sums = await query
            .Select(r => new
            {
                r.Credibility,
                r.Safety,
                r.ResponseSpeed,
                r.Transparency
            })
            .ToListAsync(ct);

        var credibility = sums.Average(s => s.Credibility);
        var safety = sums.Average(s => s.Safety);
        var responseSpeed = sums.Average(s => s.ResponseSpeed);
        var transparency = sums.Average(s => s.Transparency);
        var overall = (credibility + safety + responseSpeed + transparency) / 4.0;

        return new UserRatingAverages(
            credibility,
            safety,
            responseSpeed,
            transparency,
            overall,
            totalCount);
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
