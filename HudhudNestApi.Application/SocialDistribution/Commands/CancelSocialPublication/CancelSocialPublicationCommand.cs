using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.CancelSocialPublication;

public sealed record CancelSocialPublicationCommand(Guid PublicationId, Guid ActorUserId) : IRequest<SocialPublicationDto>;
