using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Queries.ListPublishers;

/// <summary>Spec §29: "GET /api/social-distribution/publishers" — every platform with a registered <c>ISocialPublisher</c> adapter, and what it supports.</summary>
public sealed record ListPublishersQuery : IRequest<IReadOnlyList<SocialPublisherInfoDto>>;
