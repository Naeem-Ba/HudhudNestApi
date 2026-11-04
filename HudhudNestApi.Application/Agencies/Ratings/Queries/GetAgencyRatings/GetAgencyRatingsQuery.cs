using MediatR;
using HudhudNestApi.Application.Agencies.Ratings.DTOs;

namespace HudhudNestApi.Application.Agencies.Ratings.Queries.GetAgencyRatings;

public sealed record GetAgencyRatingsQuery(Guid AgencyId, int Page = 1, int PageSize = 20)
    : IRequest<AgencyRatingSummaryDto>;
