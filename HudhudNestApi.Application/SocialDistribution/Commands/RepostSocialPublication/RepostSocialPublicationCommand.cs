using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.RepostSocialPublication;

/// <summary>
/// Phase 13 spec §"Repost": re-shares an already-Published publication's property to the SAME
/// social account as a brand-new, independent <c>SocialPublication</c> (own Id, own
/// SocialPostContent copy, own Content Version, own <c>utm_campaign=property_promotion</c>
/// attribution) — never mutates or re-queues the original. Left Draft after creation, exactly
/// like the manual "create one publication by hand" flow, so the caller must still explicitly
/// queue it (spec: "Repost يدوي" default; scheduling a repost reuses the existing
/// Queue/Reschedule endpoints — no separate scheduling path is needed for this).
/// </summary>
public sealed record RepostSocialPublicationCommand(Guid OriginalPublicationId, Guid ActorUserId) : IRequest<SocialPublicationDto>;
