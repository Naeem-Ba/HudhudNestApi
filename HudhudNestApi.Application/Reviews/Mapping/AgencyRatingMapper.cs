using HudhudNestApi.Application.Reviews.DTOs;
using HudhudNestApi.Domain.Reviews.Entities;

namespace HudhudNestApi.Application.Reviews.Mapping;

internal static class AgencyRatingMapper
{
    public static AgencyRatingDto ToDto(AgencyRating r) => new(
        Id: r.Id,
        AgencyId: r.AgencyId,
        RaterId: r.RaterId,
        RaterName: RaterName(r),
        RaterImageUrl: r.Rater?.ProfileImageUrl,
        Service: r.Service,
        Professionalism: r.Professionalism,
        Reliability: r.Reliability,
        OverallExperience: r.OverallExperience,
        OverallScore: r.OverallScore,
        Comment: r.Comment,
        CreatedAt: r.CreatedAt);

    private static string RaterName(AgencyRating r)
    {
        if (r.Rater is null)
            return "مستخدم";

        if (!string.IsNullOrWhiteSpace(r.Rater.DisplayName))
            return r.Rater.DisplayName;

        var fullName = $"{r.Rater.FirstName} {r.Rater.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? "مستخدم" : fullName;
    }
}
