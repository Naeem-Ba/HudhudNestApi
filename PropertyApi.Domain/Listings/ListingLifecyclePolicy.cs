namespace PropertyApi.Domain.Listings;

/// <summary>
/// Single source of truth for the free-listing lifecycle: how long a listing stays
/// published, when its owner is warned, how long it survives after expiry, how many
/// active listings a free-tier owner may hold, and what a single-listing extension costs.
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
    /// How many simultaneously active listings a free-tier owner may hold.
    ///
    /// "Active" means not deleted and not expired — an expired listing inside its grace
    /// window does NOT consume the quota, so an owner is never trapped between paying to
    /// extend an old listing and being unable to post a new one.
    /// </summary>
    public const int FreeTierActiveListingLimit = 1;

    /// <summary>
    /// Price, in USD, of extending one listing by one PublicationPeriod.
    /// </summary>
    public const decimal SingleListingExtensionFeeUsd = 1.00m;

    /// <summary>
    /// The moment an expired listing becomes eligible for deletion.
    /// </summary>
    public static DateTime DeletionDueAt(DateTime expiresAtUtc)
        => expiresAtUtc.Add(GracePeriodBeforeDeletion);
}
