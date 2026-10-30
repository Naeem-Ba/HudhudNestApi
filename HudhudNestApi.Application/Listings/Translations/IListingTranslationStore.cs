using HudhudNestApi.Domain.Listings.Translations;

namespace HudhudNestApi.Application.Listings.Translations;

public sealed record ListingTextSource(string Title, string Description);

public sealed record StoredListingTranslation(
    Guid Id,
    ListingTranslationField Field,
    ListingTranslationStatus Status,
    string SourceHash,
    int PolicyVersion,
    string? SourceLanguage,
    string? TranslatedText,
    int AttemptCount,
    DateTime? ClaimedUntil,
    DateTime? RetryAfter,
    string? LastError);

public sealed record TranslationClaimRequest(
    ListingTranslationField Field, string? SourceLanguage, string SourceHash, int SourceCharacters);

/// <summary>A won claim: the right to call the provider for this row until <c>ClaimedUntil</c>.</summary>
public sealed record TranslationClaim(Guid RowId, Guid Token, int AttemptsBefore);

/// <summary>
/// Persistence for listing translations. Every state change is a single atomic statement that re-checks the
/// listing (not deleted, text still hashes to the claimed value), so two instances can never both own a
/// row and a result for edited or deleted text is never committed.
/// </summary>
public interface IListingTranslationStore
{
    /// <summary>The listing's current title/description if it is publicly viewable now (the same rule as its public detail page); null if missing, deleted, unpublished, expired or taken down.</summary>
    Task<ListingTextSource?> GetPubliclyViewableTextAsync(ListingRef listing, DateTime nowUtc, CancellationToken ct);

    Task<IReadOnlyList<StoredListingTranslation>> GetAsync(ListingRef listing, string targetLanguage, CancellationToken ct);

    /// <summary>
    /// Takes ownership of the rows for (listing, field, language) that are absent, stale (other hash or
    /// policy), abandoned claims, or failures whose cooldown has passed - all fields of one request in a
    /// single transaction under one advisory lock, so two requests can never each win a different field and
    /// cause two provider calls. An entry is null when someone else owns the row, it is already valid, it is
    /// cooling down, or the listing is gone/changed.
    /// </summary>
    Task<IReadOnlyList<TranslationClaim?>> TryClaimAsync(
        ListingRef listing, string targetLanguage, IReadOnlyList<TranslationClaimRequest> requests,
        int policyVersion, string provider, TimeSpan lease, DateTime nowUtc, CancellationToken ct);

    /// <summary>Stores the result. False when the claim was lost or the listing was deleted or edited meanwhile; nothing is stored then.</summary>
    Task<bool> CompleteAsync(
        ListingRef listing, TranslationClaim claim, ListingTranslationField field, ListingTranslationStatus status,
        string? translatedText, string? sourceLanguage, DateTime nowUtc, CancellationToken ct);

    Task FailAsync(
        TranslationClaim claim, string errorCode, bool countAttempt, DateTime retryAfterUtc,
        DateTime nowUtc, CancellationToken ct);

    /// <summary>Atomically adds <paramref name="characters"/> to the month's counter unless that would pass <paramref name="limit"/>.</summary>
    Task<bool> TryReserveBudgetAsync(string month, long characters, long limit, DateTime nowUtc, CancellationToken ct);
}
