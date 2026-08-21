using MediatR;
using PropertyApi.Application.Reviews.DTOs;

namespace PropertyApi.Application.Reviews.Queries.GetUserRatings;

public sealed record GetUserRatingsQuery(
    Guid RatedUserId,
    int Page = 1,
    int PageSize = 20
) : IRequest<UserRatingSummaryDto>;
