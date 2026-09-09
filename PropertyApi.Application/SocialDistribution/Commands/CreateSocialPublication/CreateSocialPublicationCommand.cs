using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Commands.CreateSocialPublication;

/// <summary>
/// Creates a Draft SocialPublication with its SocialPostContent attached. Folds the spec's
/// separate "GenerateSocialPostContent" use case in here — content is always generated as part
/// of creating the publication it belongs to (there is no scenario in this design where a
/// publication exists without content, so a second round-trip would just be extra ceremony).
/// Title/Body/ImageUrl/Hashtags/Language are all optional: omitted fields are auto-generated
/// from the property (see the handler).
///
/// <c>DistributionRuleId</c>/<c>DistributionRunId</c> (Phase 4) are stamped by
/// <c>DistributionEngine</c> when this publication is the automatic product of rule evaluation;
/// both stay null for the manual "create one publication by hand" API flow.
/// </summary>
public sealed record CreateSocialPublicationCommand(
    Guid PropertyId,
    Guid SocialAccountId,
    Guid CreatedByUserId,
    string? Title,
    string? Body,
    string? ImageUrl,
    IReadOnlyList<string>? Hashtags,
    string Language = "ar",
    Guid? DistributionRuleId = null,
    Guid? DistributionRunId = null,
    /// <summary>Phase 13: true for a deliberate repost/promotion — see <c>SocialPublication.Create</c>'s <c>isPromotionalRepost</c> parameter. False for every pre-existing caller.</summary>
    bool IsPromotionalRepost = false) : IRequest<SocialPublicationDto>;
