using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.RejectSocialContent;

/// <summary>Human Review (Phase 8 spec §3): an admin/editor rejects a PendingReview content — it can never be queued until revised and re-approved.</summary>
public sealed record RejectSocialContentCommand(Guid PublicationId, Guid ActorUserId, string Note) : IRequest<SocialPublicationDto>;
