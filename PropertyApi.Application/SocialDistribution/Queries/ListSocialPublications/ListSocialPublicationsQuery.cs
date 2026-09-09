using MediatR;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Queries.ListSocialPublications;

public sealed record ListSocialPublicationsQuery(SocialPublicationFilterDto Filter) : IRequest<PagedResult<SocialPublicationDto>>;
