using FluentValidation;
using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;

namespace HudhudNestApi.Application.ShortStay.Queries.GetShortStayListingReviews;

/// <summary>The reviews of a published short-stay listing (public), newest first, with the average. QA Phase 9, P9-D06.</summary>
public sealed record GetShortStayListingReviewsQuery(Guid ListingId, int Page = 1, int PageSize = 10)
    : IRequest<ShortStayReviewSummaryDto>;

public sealed class GetShortStayListingReviewsQueryValidator : AbstractValidator<GetShortStayListingReviewsQuery>
{
    public GetShortStayListingReviewsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

public sealed class GetShortStayListingReviewsQueryHandler
    : IRequestHandler<GetShortStayListingReviewsQuery, ShortStayReviewSummaryDto>
{
    private readonly IShortStayListingRepository _listings;
    private readonly IShortStayReviewRepository _reviews;

    public GetShortStayListingReviewsQueryHandler(IShortStayListingRepository listings, IShortStayReviewRepository reviews)
    {
        _listings = listings;
        _reviews = reviews;
    }

    public async Task<ShortStayReviewSummaryDto> Handle(GetShortStayListingReviewsQuery request, CancellationToken ct)
    {
        // Same visibility as the listing itself: an unpublished or deleted listing has no public reviews.
        if (await _listings.GetPublishedByIdWithDetailsAsync(request.ListingId, ct) is null)
            throw new NotFoundException($"Listing {request.ListingId} was not found.");

        var (items, average, total) = await _reviews.GetPageByListingIdAsync(request.ListingId, request.Page, request.PageSize, ct);

        return new ShortStayReviewSummaryDto(
            Math.Round(average, 1),
            total,
            items.Select(r => new ShortStayReviewItemDto(
                r.Id, r.ReviewerId, r.ReviewerName, r.ReviewerImageUrl, r.Rating, r.Comment, r.CreatedAt)).ToList());
    }
}
