using MediatR;
using HudhudNestApi.Application.Listings.DTOs;

namespace HudhudNestApi.Application.Listings.Queries.GetSocialPerformance;

/// <summary>Phase 14 spec §10 — the Social Performance dashboard. <paramref name="PropertyId"/> null reports across every property; <paramref name="FromUtc"/>/<paramref name="ToUtc"/> null means unbounded on that side.</summary>
public sealed record GetSocialPerformanceQuery(Guid? PropertyId, DateTime? FromUtc, DateTime? ToUtc) : IRequest<SocialPerformanceDto>;
