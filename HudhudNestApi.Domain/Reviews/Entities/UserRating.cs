using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Domain.Reviews.Entities;

/// <summary>
/// A rating one user (RaterId) gives another user (RatedUserId) — used to build
/// the public profile page's trust score. Distinct from PropertyReview, which
/// rates a specific property, not a person.
///
/// Business rules:
/// - A user cannot rate themselves.
/// - One rating per (RaterId, RatedUserId) pair (enforced by unique DB index,
///   same pattern as PropertyReview's one-review-per-user-per-property rule).
/// - Each of the four criteria is scored 1..5.
/// </summary>
public sealed class UserRating : BaseEntity
{
    public Guid RatedUserId { get; private set; }
    public Guid RaterId { get; private set; }

    // -- Criteria (1..5 each) -------------------------------------
    public int Credibility { get; private set; }    // المصداقية
    public int Safety { get; private set; }          // الأمان
    public int ResponseSpeed { get; private set; }   // سرعة الرد
    public int Transparency { get; private set; }    // الشفافية

    public string? Comment { get; private set; }

    // EF navigation
    public UserAccount? RatedUser { get; private set; }
    public UserAccount? Rater { get; private set; }

    private UserRating() { }

    public static UserRating Create(
        Guid ratedUserId,
        Guid raterId,
        int credibility,
        int safety,
        int responseSpeed,
        int transparency,
        string? comment = null)
    {
        if (ratedUserId == Guid.Empty)
            throw new DomainException("معرّف المستخدم المُقيَّم مطلوب.");

        if (raterId == Guid.Empty)
            throw new DomainException("معرّف المُقيِّم مطلوب.");

        if (ratedUserId == raterId)
            throw new DomainException("لا يمكنك تقييم نفسك.");

        ValidateScore(credibility, "المصداقية");
        ValidateScore(safety, "الأمان");
        ValidateScore(responseSpeed, "سرعة الرد");
        ValidateScore(transparency, "الشفافية");

        return new UserRating
        {
            RatedUserId = ratedUserId,
            RaterId = raterId,
            Credibility = credibility,
            Safety = safety,
            ResponseSpeed = responseSpeed,
            Transparency = transparency,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
        };
    }

    private static void ValidateScore(int score, string fieldNameAr)
    {
        if (score is < 1 or > 5)
            throw new DomainException($"تقييم \"{fieldNameAr}\" يجب أن يكون بين 1 و5.");
    }

    /// <summary>
    /// Edits an existing rating in place (upsert path in RateUserCommandHandler) —
    /// allows a user to revise a rating they already submitted instead of being
    /// permanently locked to their first score.
    /// </summary>
    public void Update(
        int credibility,
        int safety,
        int responseSpeed,
        int transparency,
        string? comment,
        DateTime utcNow)
    {
        ValidateScore(credibility, "المصداقية");
        ValidateScore(safety, "الأمان");
        ValidateScore(responseSpeed, "سرعة الرد");
        ValidateScore(transparency, "الشفافية");

        Credibility = credibility;
        Safety = safety;
        ResponseSpeed = responseSpeed;
        Transparency = transparency;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Average of the four criteria for this single rating (used client-side
    /// and in list responses; the profile-wide average is computed by the
    /// repository across all ratings, not from this instance method).
    /// </summary>
    public double OverallScore =>
        (Credibility + Safety + ResponseSpeed + Transparency) / 4.0;
}
