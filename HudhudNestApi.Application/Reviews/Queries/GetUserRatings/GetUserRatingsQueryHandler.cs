using MediatR;
using HudhudNestApi.Application.Reviews.DTOs;
using HudhudNestApi.Application.Reviews.Interfaces;

namespace HudhudNestApi.Application.Reviews.Queries.GetUserRatings;

public sealed class GetUserRatingsQueryHandler
    : IRequestHandler<GetUserRatingsQuery, UserRatingSummaryDto>
{
    private readonly IUserRatingRepository _ratings;

    public GetUserRatingsQueryHandler(IUserRatingRepository ratings)
        => _ratings = ratings;

    public async Task<UserRatingSummaryDto> Handle(
        GetUserRatingsQuery request,
        CancellationToken ct)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);

        var averages = await _ratings.GetAveragesAsync(request.RatedUserId, ct);

        var (ratings, totalCount) = await _ratings.GetByRatedUserIdAsync(
            request.RatedUserId, page, pageSize, ct);

        var mapped = ratings
            .Select(r => new UserRatingDto(
                Id: r.Id,
                RatedUserId: r.RatedUserId,
                RaterId: r.RaterId,
                RaterName: r.Rater != null
                    ? (!string.IsNullOrWhiteSpace(r.Rater.DisplayName)
                        ? r.Rater.DisplayName
                        : $"{r.Rater.FirstName} {r.Rater.LastName}".Trim())
                    : "مستخدم",
                RaterImageUrl: r.Rater?.ProfileImageUrl,
                Credibility: r.Credibility,
                Safety: r.Safety,
                ResponseSpeed: r.ResponseSpeed,
                Transparency: r.Transparency,
                InformationAccuracy: r.InformationAccuracy,
                Conduct: r.Conduct,
                OverallScore: r.OverallScore,
                Comment: r.Comment,
                CreatedAt: r.CreatedAt))
            .ToList()
            .AsReadOnly();

        return new UserRatingSummaryDto(
            AverageCredibility: Math.Round(averages.Credibility, 1),
            AverageSafety: Math.Round(averages.Safety, 1),
            AverageResponseSpeed: Math.Round(averages.ResponseSpeed, 1),
            AverageTransparency: Math.Round(averages.Transparency, 1),
            AverageInformationAccuracy: RoundOrNull(averages.InformationAccuracy),
            AverageConduct: RoundOrNull(averages.Conduct),
            AverageOverall: Math.Round(averages.Overall, 1),
            TotalCount: totalCount,
            Page: page,
            PageSize: pageSize,
            Ratings: mapped);
    }

    private static double? RoundOrNull(double? value)
        => value.HasValue ? Math.Round(value.Value, 1) : null;
}
