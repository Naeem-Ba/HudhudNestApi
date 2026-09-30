using MediatR;
using HudhudNestApi.Application.Marketing.DTOs;

namespace HudhudNestApi.Application.Marketing.Queries.GetMarketingEventsSummary;

/// <summary>Null dates default to "the last 30 days" in the handler.</summary>
public sealed record GetMarketingEventsSummaryQuery(
    DateTime? FromUtc,
    DateTime? ToUtc) : IRequest<MarketingEventsSummaryDto>;
