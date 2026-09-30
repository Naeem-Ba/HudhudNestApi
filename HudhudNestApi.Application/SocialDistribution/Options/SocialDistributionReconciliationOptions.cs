namespace HudhudNestApi.Application.SocialDistribution.Options;

/// <summary>
/// Safety-net sweep for automatic distribution — section <c>SocialDistribution:Reconciliation</c>.
/// The <c>PropertyPublishedEvent</c> is the fast path; this sweep picks up any recently published
/// listing that never got a <c>DistributionRun</c> (a listing path that raises no event, a
/// notification lost to a crash/restart, an event handler that failed before recording a run).
///
/// <see cref="LookbackDays"/> is the guard rail that matters: the sweep must NEVER walk the whole
/// back catalogue, otherwise the first deploy (or the day an admin activates the first rule)
/// would blast every old listing onto the brand's pages. Anything published before the window is
/// only ever distributed by an explicit admin dispatch.
/// </summary>
public sealed class SocialDistributionReconciliationOptions
{
    public const string SectionName = "SocialDistribution:Reconciliation";

    public bool Enabled { get; set; } = true;

    public int LookbackDays { get; set; } = 3;

    /// <summary>Only listings published at least this long ago are picked up, so the sweep never races the in-flight event handler.</summary>
    public TimeSpan MinAge { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Caps one sweep — a backlog drains over several sweeps rather than one huge run.</summary>
    public int BatchSize { get; set; } = 10;
}
