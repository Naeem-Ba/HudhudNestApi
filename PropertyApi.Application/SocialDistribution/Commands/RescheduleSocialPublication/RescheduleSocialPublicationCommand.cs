using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Commands.RescheduleSocialPublication;

/// <summary>Phase 12 spec §12: change a still-Queued publication's scheduled time (or clear it — <c>null</c> means "as soon as possible").</summary>
public sealed record RescheduleSocialPublicationCommand(Guid PublicationId, Guid ActorUserId, DateTime? NewScheduledAt) : IRequest<SocialPublicationDto>;
