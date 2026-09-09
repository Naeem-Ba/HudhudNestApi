using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Commands.RejectSocialContent;

/// <summary>Human Review (Phase 8 spec §3): an admin/editor rejects a PendingReview content — it can never be queued until revised and re-approved.</summary>
public sealed record RejectSocialContentCommand(Guid PublicationId, Guid ActorUserId, string Note) : IRequest<SocialPublicationDto>;
