namespace PropertyApi.Domain.Plans.Entities;

/// <summary>
/// A subscription tier a user can select. This is the minimal real slice of the
/// subscription system documented (but deliberately not built) in
/// FRONTEND_BACKEND_CONTRACT.md §11 — a catalog row plus the ability to record which
/// one a user picked. It intentionally has no billing cycle, no seats, no checkout:
/// those stay out of scope until the preconditions in §11.5 are met.
///
/// Content strings are i18n *keys*, not display text — the frontend owns translation
/// (ar/en/de) exactly as documented in §11.2. Only the numbers here are real data.
/// </summary>
public class Plan
{
    public Guid Id { get; private set; }

    /// <summary>Stable machine-readable identifier: free, basic, premium, elite.</summary>
    public string Tier { get; private set; } = string.Empty;

    public string NameKey { get; private set; } = string.Empty;
    public string TaglineKey { get; private set; } = string.Empty;

    /// <summary>"free" | "monthly" | "contact" — how PriceUsd should be presented.</summary>
    public string PriceKind { get; private set; } = string.Empty;

    /// <summary>0 for the free tier, null when PriceKind is "contact".</summary>
    public decimal? PriceUsd { get; private set; }

    /// <summary>i18n keys for the feature checklist — never literal text. string[] (not
    /// IReadOnlyList) because EF Core's Npgsql primitive-collection mapping requires a
    /// mutable array shape at the property level, even though callers only read it.</summary>
    public string[] FeatureKeys { get; private set; } = Array.Empty<string>();

    /// <summary>Optional i18n key for a caveat shown outside the feature checklist.</summary>
    public string? NoteKey { get; private set; }

    public string CtaKey { get; private set; } = string.Empty;

    public bool IsRecommended { get; private set; }

    public bool IsActive { get; private set; } = true;

    public int DisplayOrder { get; private set; }

    /// <summary>
    /// Maximum simultaneously-active listings an account on this plan may hold. When the
    /// account belongs to an agency, this is the pooled limit for the whole agency, resolved
    /// from the agency owner's plan — see <c>IListingQuotaPolicy</c>'s doc comment for why
    /// the owner's plan (not each member's own) governs the agency-wide pool.
    ///
    /// Null means unlimited. That is a deliberate reading of the Elite tier's "negotiated,
    /// contact us" pricing (RELEASE-BLOCKERS-AR.md §B-3 / FRONTEND_BACKEND_CONTRACT.md §11.1):
    /// there is no per-account override mechanism yet to express an arbitrary agreed number,
    /// so the catalog row treats "contact us" as no cap rather than silently capping a paying
    /// Elite customer at some other tier's number. Revisit if/when a real Subscription entity
    /// gives Elite accounts an actual negotiated figure.
    /// </summary>
    public int? ListingLimit { get; private set; }

    private Plan() { }

    public static Plan Create(
        string tier,
        string nameKey,
        string taglineKey,
        string priceKind,
        decimal? priceUsd,
        IReadOnlyList<string> featureKeys,
        string ctaKey,
        bool isRecommended,
        int displayOrder,
        int? listingLimit,
        string? noteKey = null)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            throw new ArgumentException("Plan tier is required.", nameof(tier));
        }

        if (listingLimit is <= 0)
        {
            throw new ArgumentException(
                "Plan listing limit must be a positive number, or null for unlimited.",
                nameof(listingLimit));
        }

        return new Plan
        {
            Id = Guid.NewGuid(),
            Tier = tier.Trim().ToLowerInvariant(),
            NameKey = nameKey,
            TaglineKey = taglineKey,
            PriceKind = priceKind,
            PriceUsd = priceUsd,
            FeatureKeys = featureKeys.ToArray(),
            NoteKey = string.IsNullOrWhiteSpace(noteKey) ? null : noteKey,
            CtaKey = ctaKey,
            IsRecommended = isRecommended,
            DisplayOrder = displayOrder,
            ListingLimit = listingLimit,
            IsActive = true
        };
    }
}
