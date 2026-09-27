namespace PropertyApi.Application.SocialDistribution.Options;

/// <summary>
/// Approval policy for automatic (rule-engine-created) publications — section
/// <c>SocialDistribution:ContentReview</c>. When <see cref="RequireReviewForAutomaticPublications"/>
/// is true, <see cref="Commands.CreateSocialPublication.CreateSocialPublicationCommandHandler"/>
/// puts a rule-created publication's content into <c>ContentReviewStatus.PendingReview</c> instead
/// of the default <c>Approved</c>; <c>SocialPublication.Queue</c> already refuses to queue
/// non-Approved content (Phase 8), so this alone is enough to hold every automatic publication in
/// Draft until an admin calls the existing <c>content/approve</c> endpoint — no other code needs
/// to change. A manually-created publication (<c>DistributionRuleId == null</c>) is never affected:
/// an admin explicitly calling the create endpoint is already the deliberate action this gate
/// exists to add for the UNattended, rule-driven path.
///
/// Defaults to <c>false</c> (today's existing behavior — a rule-created publication auto-queues)
/// so shipping this capability changes nothing until the owner deliberately turns it on; recorded
/// as an open product decision in the Phase 1 audit report, not something this codebase should
/// silently flip.
/// </summary>
public sealed class SocialDistributionContentReviewOptions
{
    public const string SectionName = "SocialDistribution:ContentReview";

    public bool RequireReviewForAutomaticPublications { get; set; }
}
