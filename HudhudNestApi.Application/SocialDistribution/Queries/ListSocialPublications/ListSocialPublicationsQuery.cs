using MediatR;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Queries.ListSocialPublications;

public sealed record ListSocialPublicationsQuery(SocialPublicationFilterDto Filter) : IRequest<PagedResult<SocialPublicationDto>>;
