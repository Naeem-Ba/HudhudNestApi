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
/// - Each of the four core criteria is scored 1..5.
/// - InformationAccuracy and Conduct are optional 1..5 criteria added later:
///   ratings created before they existed (and older app builds that do not
///   send them) keep them null, and a null criterion is left out of every
///   average instead of being counted as 0.
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
    public int? InformationAccuracy { get; private set; } // دقة المعلومات (اختياري)
    public int? Conduct { get; private set; }             // حسن التعامل (اختياري)

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
        string? comment = null,
        int? informationAccuracy = null,
        int? conduct = null)
    {
        if (ratedUserId == Guid.Empty)
            throw new DomainException("معرّف المستخدم المُقيَّم مطلوب.");

        if (raterId == Guid.Empty)
            throw new DomainException("معرّف المُقيِّم مطلوب.");

        if (ratedUserId == raterId)
            throw new DomainException("لا يمكنك تقييم نفسك.");

        ValidateScores(credibility, safety, responseSpeed, transparency, informationAccuracy, conduct);

        return new UserRating
        {
            RatedUserId = ratedUserId,
            RaterId = raterId,
            Credibility = credibility,
            Safety = safety,
            ResponseSpeed = responseSpeed,
            Transparency = transparency,
            InformationAccuracy = informationAccuracy,
            Conduct = conduct,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
        };
    }

    private static void ValidateScores(
        int credibility,
        int safety,
        int responseSpeed,
        int transparency,
        int? informationAccuracy,
        int? conduct)
    {
        ValidateScore(credibility, "المصداقية");
        ValidateScore(safety, "الأمان");
        ValidateScore(responseSpeed, "سرعة الرد");
        ValidateScore(transparency, "الشفافية");

        if (informationAccuracy.HasValue)
            ValidateScore(informationAccuracy.Value, "دقة المعلومات");

        if (conduct.HasValue)
            ValidateScore(conduct.Value, "حسن التعامل");
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
        DateTime utcNow,
        int? informationAccuracy = null,
        int? conduct = null)
    {
        ValidateScores(credibility, safety, responseSpeed, transparency, informationAccuracy, conduct);

        Credibility = credibility;
        Safety = safety;
        ResponseSpeed = responseSpeed;
        Transparency = transparency;
        InformationAccuracy = informationAccuracy;
        Conduct = conduct;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Average of the criteria this rating actually scored (the four core ones
    /// plus whichever optional ones are present). The profile-wide overall is
    /// the mean of this value across all ratings, computed by the repository
    /// with the same formula in SQL — never from client-supplied numbers.
    /// </summary>
    public double OverallScore =>
        (Credibility + Safety + ResponseSpeed + Transparency
            + (InformationAccuracy ?? 0) + (Conduct ?? 0))
        / (4.0 + (InformationAccuracy.HasValue ? 1 : 0) + (Conduct.HasValue ? 1 : 0));
}
