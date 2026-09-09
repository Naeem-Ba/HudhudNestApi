namespace PropertyApi.Domain.SocialDistribution.Enums;

/// <summary>
/// Lifecycle of one <see cref="Entities.DistributionRun"/> (Phase 4 spec §13/§21). Distinct from
/// <see cref="SocialPublicationStatus"/>: a run tracks the EVALUATION and Publication-creation
/// step, not any single publish attempt.
///
/// Pending      → Evaluating
/// Evaluating   → Completed | PartiallyCompleted | Failed
/// (Created/Queued are transient sub-phases folded into Evaluating for this design — see
/// DistributionRun's remarks: nothing outside DistributionEngine ever observes a run sitting in
/// exactly "Created" or "Queued", so they are not separate persisted states here.)
/// </summary>
public enum DistributionRunStatus
{
    Pending = 1,
    Evaluating = 2,
    Created = 3,
    Queued = 4,

    /// <summary>Every matched, eligible account produced a queued SocialPublication (including the "zero rules matched" case — nothing was owed and nothing failed).</summary>
    Completed = 5,

    /// <summary>At least one matched account produced a Publication AND at least one was skipped/ineligible.</summary>
    PartiallyCompleted = 6,

    /// <summary>Either the run could not even start (e.g. property no longer public) or every matched account was skipped/ineligible.</summary>
    Failed = 7,

    Cancelled = 8,
}
