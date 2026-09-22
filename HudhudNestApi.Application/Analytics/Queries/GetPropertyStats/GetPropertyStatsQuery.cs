using MediatR;
using HudhudNestApi.Application.Analytics.DTOs;

namespace HudhudNestApi.Application.Analytics.Queries.GetPropertyStats;

/// <summary>Statistics for a single property â€” shown to the owner.</summary>
public sealed record GetPropertyStatsQuery(
    Guid PropertyId,
    Guid OwnerId
) : IRequest<PropertyStatsDto>;
