using MediatR;
using HudhudNestApi.Application.Reviews.DTOs;

namespace HudhudNestApi.Application.Reviews.Commands.RateUser;

public sealed record RateUserCommand(
    Guid RatedUserId,
    Guid RaterId,
    int Credibility,
    int Safety,
    int ResponseSpeed,
    int Transparency,
    string? Comment,
    int? InformationAccuracy = null,
    int? Conduct = null
) : IRequest<UserRatingDto>;
