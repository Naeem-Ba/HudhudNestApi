namespace PropertyApi.Application.Listings.Interfaces;

/// <summary>
/// Exposes the free-tier listing quota as live configuration instead of a compiled
/// constant, so operations can raise or lower it without a redeploy.
///
/// Backed by IOptionsMonitor in Infrastructure (ASP.NET Core reloads appsettings.json
/// automatically), so a value edited on disk takes effect on the next request — Application
/// only ever sees the current value and never depends on the configuration/options
/// packages directly.
/// </summary>
public interface IListingQuotaPolicy
{
    /// <summary>
    /// Maximum active listings an agency may hold at once, pooled across every member's
    /// listings together — not divided per member. See RELEASE-BLOCKERS-AR.md B-3: an
    /// agency owner decides internally which members actually publish, so the cap belongs
    /// to the agency account as a whole, not to each head that happens to belong to it.
    /// Individual, non-agency owners are unaffected and keep
    /// <see cref="PropertyApi.Domain.Listings.ListingLifecyclePolicy.FreeTierActiveListingLimit"/>.
    /// </summary>
    int AgencyActiveListingLimit { get; }
}
