using MediatR;
using HudhudNestApi.Application.Reviews.DTOs;

namespace HudhudNestApi.Application.Reviews.Commands.RateAgency;

public sealed record RateAgencyCommand(
    Guid AgencyId,
    Guid RaterId,
    int Service,
    int Professionalism,
    int Reliability,
    int OverallExperience,
    string? Comment) : IRequest<AgencyRatingDto>;
