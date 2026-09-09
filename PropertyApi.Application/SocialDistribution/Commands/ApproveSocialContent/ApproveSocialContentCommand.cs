using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Commands.ApproveSocialContent;

/// <summary>Human Review (Phase 8 spec §3): an admin/editor confirms a PendingReview/Rejected content is safe to queue.</summary>
public sealed record ApproveSocialContentCommand(Guid PublicationId, Guid ActorUserId, string? Note) : IRequest<SocialPublicationDto>;
