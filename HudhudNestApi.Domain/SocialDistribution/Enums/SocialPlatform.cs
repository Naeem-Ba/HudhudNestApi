namespace HudhudNestApi.Domain.SocialDistribution.Enums;

/// <summary>
/// A social media platform HudhudNest can distribute listings to through its own accounts.
///
/// Deliberately a DIFFERENT type from <see cref="Listings.Enums.SharePlatform"/> (Phase 1/2):
/// SharePlatform describes a VISITOR's manual share action (they tap "WhatsApp" and their own
/// phone opens WhatsApp) — there is no HudhudNest-owned account involved and "Native"/"CopyLink"/
/// "Other" make sense there. SocialPlatform describes an actual company-operated channel this
/// bounded context posts to on HudhudNest's own behalf; "Native"/"CopyLink" have no meaning here,
/// and new entries (Instagram/TikTok/YouTube/LinkedIn) don't belong on the sharing enum at all
/// since a visitor's device can't natively "share to YouTube". Keeping them separate is the
/// bounded-context separation the spec requires (§1): merging them would leak Social Sharing's
/// vocabulary into Social Distribution's and vice versa.
/// </summary>
public enum SocialPlatform
{
    Facebook = 1,
    Instagram = 2,
    Telegram = 3,
    TikTok = 4,
    YouTube = 5,
    LinkedIn = 6,
}
