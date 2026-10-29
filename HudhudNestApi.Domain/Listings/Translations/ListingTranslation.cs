using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Domain.Listings.Translations;

/// <summary>The listing text fields that may be machine-translated. Nothing structured (price, area, ids...) is ever listed here.</summary>
public enum ListingTranslationField
{
    Title = 1,
    Description = 2
}

public enum ListingTranslationStatus
{
    /// <summary>Claimed by one request; the provider call is in flight. Becomes reclaimable once <see cref="ListingTranslation.ClaimedUntil"/> passes.</summary>
    Pending = 1,

    /// <summary><see cref="ListingTranslation.TranslatedText"/> holds a non-empty translation of the text whose hash is <see cref="ListingTranslation.SourceHash"/>.</summary>
    Completed = 2,

    /// <summary>The provider reported the text is already in the target language; kept so the provider is not asked again.</summary>
    SameLanguage = 3,

    /// <summary>The last attempt failed; no new attempt before <see cref="ListingTranslation.RetryAfter"/>.</summary>
    Failed = 4
}

/// <summary>
/// One machine translation of one text field of a listing into one language. There is exactly one row per
/// (listing, field, target language): when the owner edits the text, the next request re-claims the same
/// row for the new text instead of adding another one, so a row can never outlive its listing's
/// relevance (the foreign key cascades on a hard delete).
///
/// A row is only ever *served* while <see cref="SourceHash"/> equals the SHA-256 of the listing's current
/// stored text and <see cref="PolicyVersion"/> is the current one - validity is decided at read time, so a
/// stale translation cannot be shown even if no invalidation ran.
///
/// Deliberately not a <c>BaseEntity</c>: no soft delete. Translations are derived data and are removed
/// with the listing, not hidden.
/// </summary>
public sealed class ListingTranslation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PropertyId { get; set; }
    public Property? Property { get; set; }
    public ListingTranslationField Field { get; set; }

    /// <summary>"ar", "de" or "en".</summary>
    public string TargetLanguage { get; set; } = string.Empty;

    /// <summary>Language the source text was in (detected locally or reported by the provider); null while unknown.</summary>
    public string? SourceLanguage { get; set; }

    /// <summary>Lower-case hex SHA-256 of the UTF-8 bytes of the exact stored source text (no normalisation).</summary>
    public string SourceHash { get; set; } = string.Empty;

    /// <summary>Bumped when the translation policy changes in a way that must invalidate every stored row.</summary>
    public int PolicyVersion { get; set; }

    /// <summary>Name of the provider that produced (or is producing) the text; never a key or endpoint.</summary>
    public string Provider { get; set; } = string.Empty;

    public ListingTranslationStatus Status { get; set; }
    public string? TranslatedText { get; set; }

    /// <summary>Length of the source text in characters; what the monthly budget was charged for this row.</summary>
    public int SourceCharacters { get; set; }

    public int AttemptCount { get; set; }
    public Guid? ClaimToken { get; set; }
    public DateTime? ClaimedUntil { get; set; }
    public DateTime? RetryAfter { get; set; }

    /// <summary>Short error category (never provider text, never listing text).</summary>
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Characters sent to the paid-or-quota-limited provider in one UTC month ("yyyy-MM"); the hard stop for the monthly cap.</summary>
public sealed class TranslationMonthlyUsage
{
    public string Month { get; set; } = string.Empty;
    public long Characters { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
