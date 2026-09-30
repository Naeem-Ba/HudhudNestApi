namespace HudhudNestApi.Domain.SocialDistribution.Enums;

/// <summary>Lifecycle of a generated <see cref="Entities.SocialMediaAsset"/> (Phase 7 spec §22).</summary>
public enum SocialMediaAssetStatus
{
    Pending = 1,
    Generating = 2,
    Generated = 3,
    Failed = 4,

    /// <summary>Kept for historical/audit purposes but no longer guaranteed reusable (e.g. its storage object was purged).</summary>
    Expired = 5,
}
