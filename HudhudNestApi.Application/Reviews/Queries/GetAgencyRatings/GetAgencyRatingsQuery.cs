using MediatR;
using HudhudNestApi.Application.Reviews.DTOs;

namespace HudhudNestApi.Application.Reviews.Queries.GetAgencyRatings;

public sealed record GetAgencyRatingsQuery(Guid AgencyId, int Page = 1, int PageSize = 20)
    : IRequest<AgencyRatingSummaryDto>;
