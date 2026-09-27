namespace PropertyApi.Domain.SocialDistribution.Enums;

/// <summary>
/// What caused a <see cref="Entities.DistributionRun"/> to start (Phase 4 spec §9/§13). Purely
/// informational/audit — evaluation and matching behave identically regardless of trigger.
/// </summary>
public enum DistributionRunTriggerType
{
    /// <summary>Property transitioned Unpublished → Published — see PropertyPublishedEvent.</summary>
    PropertyPublished = 1,

    /// <summary>An admin explicitly called POST /api/social-distribution/dispatch/{propertyId}.</summary>
    Manual = 2,

    /// <summary>Reserved for a future time-based trigger (not wired to anything today).</summary>
    Scheduled = 3,

    /// <summary>An admin re-ran distribution for a property that was already distributed once.</summary>
    Republish = 4,

    /// <summary>Reserved: re-running just the evaluation/creation step after a prior run failed outright (distinct from SocialPublication.RetryManually, which retries one already-created publication).</summary>
    Retry = 5,

    /// <summary>The background reconciliation sweep found a recently published listing that no run had ever evaluated — see SocialDistributionReconciliationOptions.</summary>
    Reconciliation = 6,
}
