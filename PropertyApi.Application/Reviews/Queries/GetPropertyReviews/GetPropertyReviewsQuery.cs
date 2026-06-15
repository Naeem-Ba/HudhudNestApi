using MediatR;
using PropertyApi.Application.Reviews.DTOs;

namespace PropertyApi.Application.Reviews.Queries.GetPropertyReviews;

public sealed record GetPropertyReviewsQuery(
    Guid PropertyId,
    int  Page     = 1,
    int  PageSize = 10
) : IRequest<PropertyReviewSummaryDto>;
