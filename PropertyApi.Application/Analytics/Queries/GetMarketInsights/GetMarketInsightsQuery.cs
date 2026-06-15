using MediatR;
using PropertyApi.Application.Analytics.DTOs;

namespace PropertyApi.Application.Analytics.Queries.GetMarketInsights;

/// <summary>Public market insights â€” no auth required.</summary>
public sealed record GetMarketInsightsQuery(
    string? CountryCode = null
) : IRequest<MarketInsightsDto>;
