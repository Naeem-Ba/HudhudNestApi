namespace HudhudNestApi.Application.Listings.Translations;

public enum TranslationFailureKind
{
    /// <summary>Network error, 5xx, rate limit: worth retrying later with the same input.</summary>
    Transient = 1,

    /// <summary>The provider rejected the input or configuration: retrying the same input will not help.</summary>
    Permanent = 2,

    /// <summary>The provider account's own quota is exhausted.</summary>
    QuotaExceeded = 3,

    Timeout = 4
}

public sealed record TranslatedText(string Text, string? DetectedSourceLanguage);

/// <summary>Either <see cref="Items"/> (same order and count as the input) or a <see cref="Failure"/>.</summary>
public sealed record TranslationBatchResult(IReadOnlyList<TranslatedText>? Items, TranslationFailureKind? Failure)
{
    public static TranslationBatchResult Success(IReadOnlyList<TranslatedText> items) => new(items, null);
    public static TranslationBatchResult Fail(TranslationFailureKind kind) => new(null, kind);
}

/// <summary>
/// The only seam to a machine-translation vendor. Implementations live in Infrastructure, are reached
/// only from the API (never from a browser or app), and must not log texts or keys. Swapping vendors means
/// writing one class and changing configuration; listing code and the UI do not change.
/// </summary>
public interface ITranslationProvider
{
    /// <summary>Short stable identifier stored with each translation, e.g. "deepl". Never an endpoint or a key.</summary>
    string Name { get; }

    /// <summary>False when no vendor is configured (no key): the service then never claims a row or spends a character.</summary>
    bool IsAvailable { get; }

    /// <summary><paramref name="sourceLanguage"/> null lets the vendor detect it. Languages are "ar", "de", "en".</summary>
    Task<TranslationBatchResult> TranslateAsync(
        IReadOnlyList<string> texts,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken ct);
}
