using MediatR;
using HudhudNestApi.Application.Listings.DTOs;

namespace HudhudNestApi.Application.Listings.Queries.GetMyPendingFeaturedRequests;

/// <summary>The caller's featured-placement requests that are waiting for payment confirmation.</summary>
public sealed record GetMyPendingFeaturedRequestsQuery(Guid OwnerId)
    : IRequest<IReadOnlyList<PendingFeaturedRequestSummaryDto>>;
