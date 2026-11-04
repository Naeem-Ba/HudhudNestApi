using HudhudNestApi.Application.Agencies.Ratings.DTOs;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Agencies.Ratings;

/// <summary>Who may not rate an office: its owner and every member of it.</summary>
public static class AgencyRatingPolicy
{
    public static bool IsMemberOrOwner(Agency agency, UserAccount user)
        => agency.OwnerUserId == user.Id || user.AgencyId == agency.Id;
}

internal static class AgencyRatingMapper
{
    public static AgencyRatingDto ToDto(AgencyRating rating, UserAccount? rater) => new(
        Id: rating.Id,
        AgencyId: rating.AgencyId,
        RaterId: rating.RaterId,
        RaterName: DisplayName(rater),
        RaterImageUrl: rater?.ProfileImageUrl,
        Service: rating.Service,
        Professionalism: rating.Professionalism,
        Reliability: rating.Reliability,
        OverallExperience: rating.OverallExperience,
        OverallScore: rating.OverallScore,
        Comment: rating.Comment,
        CreatedAt: rating.CreatedAt);

    public static string DisplayName(UserAccount? account)
    {
        if (account is null)
            return "مستخدم";

        if (!string.IsNullOrWhiteSpace(account.DisplayName))
            return account.DisplayName;

        var full = $"{account.FirstName} {account.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? "مستخدم" : full;
    }
}
