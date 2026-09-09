using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Commands.CancelSocialPublication;

public sealed record CancelSocialPublicationCommand(Guid PublicationId, Guid ActorUserId) : IRequest<SocialPublicationDto>;
