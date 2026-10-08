using MediatR;
using HudhudNestApi.Application.Reviews.DTOs;

namespace HudhudNestApi.Application.Reviews.Queries.GetAgencyRatingEligibility;

public sealed record GetAgencyRatingEligibilityQuery(Guid AgencyId, Guid RaterId)
    : IRequest<AgencyRatingEligibilityDto>;
