namespace PropertyApi.Infrastructure.Listings;

/// <summary>
/// Interim, config-driven mitigation for RELEASE-BLOCKERS-AR.md B-3 (the free-tier
/// single-listing cap makes a real agency unusable). Not a substitute for a real
/// Subscription entity — that remains a product decision, deliberately deferred — but a
/// number ops can raise or lower without shipping code, until plans exist.
/// </summary>
public sealed class ListingQuotaOptions
{
    public const string SectionName = "Listings:Quota";

    /// <summary>
    /// Maximum active listings an agency may hold at once, pooled across every member —
    /// not per member (an owner may have agents who never publish at all). Bound via
    /// IOptionsMonitor, so a value edited in appsettings.json takes effect on the next
    /// request without a redeploy.
    /// </summary>
    public int AgencyActiveListingLimit { get; set; } = 50;
}
