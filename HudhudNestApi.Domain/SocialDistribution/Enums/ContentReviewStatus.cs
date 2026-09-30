namespace HudhudNestApi.Domain.SocialDistribution.Enums;

/// <summary>
/// Human-review workflow for a <see cref="Entities.SocialPostContent"/> (Phase 8 spec §3:
/// "Human Review أو Auto Approval حسب السياسة"). Defaults to <see cref="Approved"/> at creation
/// — today's only content generator (<c>TemplateSocialContentGenerator</c>) builds text strictly
/// from Domain facts and never needs a human in the loop, so nothing in this codebase currently
/// puts a content row into <see cref="PendingReview"/>. The state exists so a future AI-assisted
/// generator (spec: "أول نشر لحساب جديد، حملة مدفوعة، محتوى حساس ⇒ يتطلب مراجعة") can require
/// review without any change to <see cref="Entities.SocialPublication.Queue"/>, which already
/// refuses to queue non-<see cref="Approved"/> content.
/// </summary>
public enum ContentReviewStatus
{
    Approved = 0,
    PendingReview = 1,
    Rejected = 2,
}
