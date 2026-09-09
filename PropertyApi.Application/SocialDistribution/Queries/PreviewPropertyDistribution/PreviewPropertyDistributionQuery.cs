using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Queries.PreviewPropertyDistribution;

/// <summary>Read-only rule evaluation for one property — spec §18's "POST /evaluate/{propertyId}" / "معاينة القواعد المطابقة لعقار معين". Modeled as a query (no side effects) even though the controller exposes it via POST, matching the spec's literal endpoint list.</summary>
public sealed record PreviewPropertyDistributionQuery(Guid PropertyId) : IRequest<DistributionPreviewDto>;
