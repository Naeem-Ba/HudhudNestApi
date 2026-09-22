using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Queries.GetSocialDashboardSummary;

/// <summary>Phase 10 spec §6 — the Social Media Admin dashboard's headline tiles.</summary>
public sealed record GetSocialDashboardSummaryQuery : IRequest<SocialDashboardSummaryDto>;
