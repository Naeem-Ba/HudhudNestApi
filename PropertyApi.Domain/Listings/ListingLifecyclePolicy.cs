namespace PropertyApi.Domain.Listings;

/// <summary>
/// Single source of truth for the free-listing lifecycle: how long a listing stays
/// published, when its owner is warned, how long it survives after expiry, and what a
/// single-listing extension costs.
///
/// Does NOT govern how many active listings an owner may hold — that quota is
/// plan-driven (Plan.ListingLimit via IListingQuotaPolicy, BACKEND-ISSUES.md §B-3) since
/// it varies per plan, unlike the terms below which are the same for every account. This
/// type used to carry a FreeTierActiveListingLimit constant; it was retired so
/// Plan.ListingLimit would be the only place that number lives.
///
/// These are business terms that the public pricing page states to users, so they must
/// not drift between the page, the scheduler, and the command handlers. They live in
/// Domain (which has zero dependencies) so every layer can read them from one place.
///
/// Deliberately constants rather than IOptions: the Application layer in this solution
/// takes no dependency on Microsoft.Extensions.Options (see PropertyApi.Application.csproj),
/// and PublishPropertyCommandHandler already established the "policy value as a static
/// readonly on the type that enforces it" convention. Changing a term here is a code change
/// and therefore a reviewed change — which is the correct bar for a value a customer was
/// shown before they paid.
/// </summary>
public static class ListingLifecyclePolicy
{
    /// <summary>
    /// How long a listing stays published from the moment it is published. Also the
    /// length granted by a paid extension.
    /// </summary>
    public static readonly TimeSpan PublicationPeriod = TimeSpan.FromDays(90);

    /// <summary>
    /// How far ahead of ExpiresAt the owner is warned. One warning per publication
    /// period — Property.ExpiryWarningSentAt is the idempotency stamp.
    /// </summary>
    public static readonly TimeSpan ExpiryWarningLeadTime = TimeSpan.FromDays(7);

    /// <summary>
    /// How long an expired listing survives, hidden, before it is deleted. The listing
    /// is recoverable by a paid extension for this entire window.
    /// </summary>
    public static readonly TimeSpan GracePeriodBeforeDeletion = TimeSpan.FromDays(30);

    /// <summary>
    /// Price, in USD, of extending one listing by one PublicationPeriod.
    /// </summary>
    public const decimal SingleListingExtensionFeeUsd = 1.00m;

    /// <summary>
    /// The moment an expired listing becomes eligible for deletion.
    /// </summary>
    public static DateTime DeletionDueAt(DateTime expiresAtUtc)
        => expiresAtUtc.Add(GracePeriodBeforeDeletion);

    // -- Featured placement --------------------------------------
    //
    // Same reasoning as above: a customer is shown this price and this duration before
    // they pay, so both belong next to the terms they are sold alongside.

    /// <summary>
    /// How long one paid featured placement lasts. Shorter than PublicationPeriod on
    /// purpose — promotion is a boost, not the listing's lifetime, and a placement that
    /// outlived the listing itself would be sold time that cannot be delivered.
    /// </summary>
    public static readonly TimeSpan FeaturedPeriod = TimeSpan.FromDays(30);

    /// <summary>
    /// Price, in USD, of promoting one listing for one FeaturedPeriod.
    /// </summary>
    public const decimal FeaturedListingFeeUsd = 5.00m;
}
