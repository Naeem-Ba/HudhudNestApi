using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.QueueSocialPublication;

/// <summary>Draft → Queued. <paramref name="ScheduledAt"/> null means "publish as soon as possible".</summary>
public sealed record QueueSocialPublicationCommand(
    Guid PublicationId,
    Guid ActorUserId,
    DateTime? ScheduledAt) : IRequest<SocialPublicationDto>;
