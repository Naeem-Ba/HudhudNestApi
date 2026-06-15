using MediatR;
using PropertyApi.Application.Analytics.DTOs;

namespace PropertyApi.Application.Analytics.Queries.GetPropertyStats;

/// <summary>Statistics for a single property â€” shown to the owner.</summary>
public sealed record GetPropertyStatsQuery(
    Guid PropertyId,
    Guid OwnerId
) : IRequest<PropertyStatsDto>;
