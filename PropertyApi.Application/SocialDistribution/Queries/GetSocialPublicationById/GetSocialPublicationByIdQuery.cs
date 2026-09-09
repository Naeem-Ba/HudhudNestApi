using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Queries.GetSocialPublicationById;

public sealed record GetSocialPublicationByIdQuery(Guid PublicationId) : IRequest<SocialPublicationDto>;
