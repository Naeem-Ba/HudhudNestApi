using PropertyApi.Domain.Reviews.Entities;

namespace PropertyApi.Application.Reviews.Interfaces;

/// <summary>
/// Aggregated per-criterion averages for a rated user's profile page.
/// All averages are on the 1..5 scale; 0 when the user has no ratings yet
/// (callers should treat TotalCount == 0 as "not yet rated" rather than
/// showing a literal 0/5 badge).
/// </summary>
public sealed record UserRatingAverages(
    double Credibility,
    double Safety,
    double ResponseSpeed,
    double Transparency,
    double Overall,
    int TotalCount);

public interface IUserRatingRepository
{
    Task AddAsync(UserRating rating, CancellationToken ct = default);

    void Update(UserRating rating);

    Task<bool> HasRatedAsync(Guid ratedUserId, Guid raterId, CancellationToken ct = default);

    /// <summary>
    /// يجلب تقييم مُقيِّم معيّن لمستخدم معيّن (Tracked — قابل للتعديل مباشرة)،
    /// يُستخدم لتفعيل تعديل التقييم بعد إرساله (عرض النموذج مُعبَّأً مسبقًا +
    /// منطق upsert في RateUserCommandHandler).
    /// </summary>
    Task<UserRating?> GetByRaterAndRatedAsync(
        Guid ratedUserId,
        Guid raterId,
        CancellationToken ct = default);

    Task<UserRatingAverages> GetAveragesAsync(Guid ratedUserId, CancellationToken ct = default);

    Task<(IReadOnlyList<UserRating> Ratings, int TotalCount)> GetByRatedUserIdAsync(
        Guid ratedUserId,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
