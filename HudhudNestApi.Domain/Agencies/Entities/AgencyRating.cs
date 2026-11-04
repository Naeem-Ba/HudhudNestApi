using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Domain.Agencies.Entities;

/// <summary>
/// A rating one user (RaterId) gives a real-estate office (AgencyId) as a business. It is
/// deliberately separate from <c>UserRating</c> (a person) and <c>PropertyReview</c> (one listing):
/// the office page shows its own score, and members of the office can never rate it.
///
/// Business rules (the application layer enforces the ones that need other data):
/// - One rating per (AgencyId, RaterId), enforced by a unique index; a second submit edits in place.
/// - Four criteria, each scored 1..5; OverallScore is their plain mean.
/// - The owner and the members of the office cannot rate it, and the rater must have had a real
///   interaction (a completed visit or a message about one of its listings) - both checked in
///   <c>RateAgencyCommandHandler</c>, since they depend on data this entity does not hold.
/// </summary>
public sealed class AgencyRating : BaseEntity
{
    public const int CommentMaxLength = 1000;

    public Guid AgencyId { get; private set; }
    public Guid RaterId { get; private set; }

    public int Service { get; private set; }
    public int Professionalism { get; private set; }
    public int Reliability { get; private set; }
    public int OverallExperience { get; private set; }

    public string? Comment { get; private set; }

    // EF navigation
    public Agency? Agency { get; private set; }
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
        var cleanComment = NormalizeComment(comment);

        return new AgencyRating
        {
            AgencyId = agencyId,
            RaterId = raterId,
            Service = service,
            Professionalism = professionalism,
            Reliability = reliability,
            OverallExperience = overallExperience,
            Comment = cleanComment,
        };
    }

    /// <summary>Edits the rating in place (the upsert path), so a rater can revise their score.</summary>
    public void Update(
        int service,
        int professionalism,
        int reliability,
        int overallExperience,
        string? comment,
        DateTime utcNow)
    {
        ValidateScores(service, professionalism, reliability, overallExperience);
        var cleanComment = NormalizeComment(comment);

        Service = service;
        Professionalism = professionalism;
        Reliability = reliability;
        OverallExperience = overallExperience;
        Comment = cleanComment;
        UpdatedAt = utcNow;
    }

    /// <summary>Mean of the four criteria; the repository computes the same formula in SQL.</summary>
    public double OverallScore => (Service + Professionalism + Reliability + OverallExperience) / 4.0;

    private static string? NormalizeComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return null;

        var trimmed = comment.Trim();
        if (trimmed.Length > CommentMaxLength)
            throw new DomainException($"التعليق يجب ألا يتجاوز {CommentMaxLength} حرفًا.");

        return trimmed;
    }

    private static void ValidateScores(int service, int professionalism, int reliability, int overallExperience)
    {
        ValidateScore(service, "جودة الخدمة");
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
