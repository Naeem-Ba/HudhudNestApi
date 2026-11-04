using MediatR;
using HudhudNestApi.Application.Agencies.Ratings.DTOs;

namespace HudhudNestApi.Application.Agencies.Ratings.Commands.RateAgency;

public sealed record RateAgencyCommand(
    Guid AgencyId,
    Guid RaterId,
    int Service,
    int Professionalism,
    int Reliability,
    int OverallExperience,
    string? Comment
) : IRequest<RateAgencyResult>;

/// <summary>The stored rating plus whether this call created it (201) or edited it (200).</summary>
public sealed record RateAgencyResult(AgencyRatingDto Rating, bool Created);
