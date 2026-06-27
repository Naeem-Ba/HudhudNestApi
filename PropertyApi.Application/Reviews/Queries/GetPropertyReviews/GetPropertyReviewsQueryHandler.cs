using MediatR;
using PropertyApi.Application.Reviews.DTOs;
using PropertyApi.Application.Reviews.Interfaces;

namespace PropertyApi.Application.Reviews.Queries.GetPropertyReviews;

public sealed class GetPropertyReviewsQueryHandler
    : IRequestHandler<GetPropertyReviewsQuery, PropertyReviewSummaryDto>
{
    private readonly IPropertyReviewRepository _reviews;

    public GetPropertyReviewsQueryHandler(IPropertyReviewRepository reviews)
        => _reviews = reviews;

    public async Task<PropertyReviewSummaryDto> Handle(
        GetPropertyReviewsQuery request,
        CancellationToken ct)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);

        var (reviews, average, totalCount) = await _reviews.GetByPropertyIdAsync(
            request.PropertyId,
            page,
            pageSize,
            ct);

        var paged = reviews
            .Select(r => new PropertyReviewDto(
                Id: r.Id,
                PropertyId: r.PropertyId,
                ReviewerId: r.ReviewerId,
                ReviewerName: r.Reviewer != null
                    ? $"{r.Reviewer.FirstName} {r.Reviewer.LastName}".Trim()
                    : "مجهول",
                ReviewerImageUrl: r.Reviewer?.ProfileImageUrl,
                Rating: r.Rating,
                Comment: r.Comment,
                CreatedAt: r.CreatedAt))
            .ToList()
            .AsReadOnly();

        return new PropertyReviewSummaryDto(
            AverageRating: Math.Round(average, 1),
            TotalCount: totalCount,
            Reviews: paged);
    }
}
