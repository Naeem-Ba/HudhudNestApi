using MediatR;
using HudhudNestApi.Application.Analytics.DTOs;

namespace HudhudNestApi.Application.Analytics.Queries.GetMarketInsights;

/// <summary>Public market insights â€” no auth required.</summary>
public sealed record GetMarketInsightsQuery(
    string? CountryCode = null
) : IRequest<MarketInsightsDto>;
