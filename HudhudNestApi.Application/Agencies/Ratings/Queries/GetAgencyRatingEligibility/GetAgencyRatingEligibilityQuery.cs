using MediatR;
using HudhudNestApi.Application.Agencies.Ratings.DTOs;

namespace HudhudNestApi.Application.Agencies.Ratings.Queries.GetAgencyRatingEligibility;

public sealed record GetAgencyRatingEligibilityQuery(Guid AgencyId, Guid RaterId)
    : IRequest<AgencyRatingEligibilityDto>;
