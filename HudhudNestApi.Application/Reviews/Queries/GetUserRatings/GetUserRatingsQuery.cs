using MediatR;
using HudhudNestApi.Application.Reviews.DTOs;

namespace HudhudNestApi.Application.Reviews.Queries.GetUserRatings;

public sealed record GetUserRatingsQuery(
    Guid RatedUserId,
    int Page = 1,
    int PageSize = 20
) : IRequest<UserRatingSummaryDto>;
