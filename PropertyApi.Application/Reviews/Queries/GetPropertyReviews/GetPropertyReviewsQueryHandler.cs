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
        GetPropertyReviewsQuery request, CancellationToken ct)
    {
        var (reviews, average) = await _reviews.GetByPropertyIdAsync(request.PropertyId, ct);

        var paged = reviews
            .OrderByDescending(r => r.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(r => new PropertyReviewDto(
                Id:               r.Id,
                PropertyId:       r.PropertyId,
                ReviewerId:       r.ReviewerId,
                ReviewerName:     r.Reviewer != null
                    ? $"{r.Reviewer.FirstName} {r.Reviewer.LastName}".Trim()
                    : "Ù…Ø¬Ù‡ÙˆÙ„",
                ReviewerImageUrl: r.Reviewer?.ProfileImageUrl,
                Rating:           r.Rating,
                Comment:          r.Comment,
                CreatedAt:        r.CreatedAt))
            .ToList()
            .AsReadOnly();

        return new PropertyReviewSummaryDto(
            AverageRating: Math.Round(average, 1),
            TotalCount:    reviews.Count,
            Reviews:       paged);
    }
}
