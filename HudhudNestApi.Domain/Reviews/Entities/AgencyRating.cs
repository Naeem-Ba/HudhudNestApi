using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Domain.Reviews.Entities;

/// <summary>
/// A customer's rating of an AGENCY as a business. Deliberately a separate aggregate from
/// <see cref="UserRating"/> (the rating of a person, e.g. the member who published the listing):
/// neither feeds the other. One rating per (agency, rater); the rater edits it in place.
/// </summary>
public sealed class AgencyRating : BaseEntity
{
    public Guid AgencyId { get; private set; }
    public Guid RaterId { get; private set; }

    // -- Criteria (1..5 each) -------------------------------------
    public int Service { get; private set; }
    public int Professionalism { get; private set; }
    public int Reliability { get; private set; }
    public int OverallExperience { get; private set; }

    public string? Comment { get; private set; }

    public UserAccount? Rater { get; private set; }

    private AgencyRating() { }

    public static AgencyRating Create(
        Guid agencyId,
        Guid raterId,
        int service,
        int professionalism,
        int reliability,
        int overallExperience,
        string? comment = null)
    {
        if (agencyId == Guid.Empty)
            throw new DomainException("معرّف المكتب مطلوب.");
        if (raterId == Guid.Empty)
            throw new DomainException("معرّف المُقيِّم مطلوب.");

        ValidateScores(service, professionalism, reliability, overallExperience);

        return new AgencyRating
        {
            AgencyId = agencyId,
            RaterId = raterId,
            Service = service,
            Professionalism = professionalism,
            Reliability = reliability,
            OverallExperience = overallExperience,
            Comment = Clean(comment),
        };
    }

    public void Update(
        int service,
        int professionalism,
        int reliability,
        int overallExperience,
        string? comment,
        DateTime utcNow)
    {
        ValidateScores(service, professionalism, reliability, overallExperience);

        Service = service;
        Professionalism = professionalism;
        Reliability = reliability;
        OverallExperience = overallExperience;
        Comment = Clean(comment);
        UpdatedAt = utcNow;
    }

    /// <summary>Mean of the four criteria.</summary>
    public double OverallScore => (Service + Professionalism + Reliability + OverallExperience) / 4.0;

    private static string? Clean(string? comment) =>
        string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

    private static void ValidateScores(int service, int professionalism, int reliability, int overallExperience)
    {
        ValidateScore(service, "الخدمة");
        ValidateScore(professionalism, "الاحترافية");
        ValidateScore(reliability, "الموثوقية");
        ValidateScore(overallExperience, "التجربة العامة");
    }

    private static void ValidateScore(int score, string fieldNameAr)
    {
        if (score is < 1 or > 5)
            throw new DomainException($"تقييم \"{fieldNameAr}\" يجب أن يكون بين 1 و5.");
    }
}
