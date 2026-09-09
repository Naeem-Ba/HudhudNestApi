using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Queries.GetSocialDashboardSummary;

/// <summary>Phase 10 spec §6 — the Social Media Admin dashboard's headline tiles.</summary>
public sealed record GetSocialDashboardSummaryQuery : IRequest<SocialDashboardSummaryDto>;
