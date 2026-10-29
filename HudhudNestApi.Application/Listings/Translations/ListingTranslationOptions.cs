namespace HudhudNestApi.Application.Listings.Translations;

/// <summary>
/// Settings for automatic listing translation (config section "ListingTranslation"). Safe defaults: the
/// feature is on for reading, but no provider is configured, so nothing is ever sent anywhere until a
/// provider and its key are set on the server. The key itself is never part of these options' logging.
/// </summary>
public sealed class ListingTranslationOptions
{
    public const string SectionName = "ListingTranslation";

    /// <summary>Master switch. Off: the endpoint answers "disabled" and touches neither the provider nor the database.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Kill switch for NEW translations only. Off: stored translations are still served, nothing new is requested.</summary>
    public bool CreationEnabled { get; set; } = true;

    /// <summary>"None" (default), "DeepL". Anything else is treated as "None".</summary>
    public string Provider { get; set; } = "None";

    /// <summary>Wall-clock limit for one provider call; the request waits at most this long.</summary>
    public int ProviderTimeoutSeconds { get; set; } = 6;

    /// <summary>Longer text is not translated (the listing validators cap descriptions at 5000).</summary>
    public int MaxFieldCharacters { get; set; } = 6000;

    /// <summary>Hard stop for characters sent to the provider per UTC month. Default sits under DeepL API Free's 500,000.</summary>
    public long MonthlyCharacterLimit { get; set; } = 400_000;

    /// <summary>How long one request owns a translation before another may take it over (covers a crashed or restarted instance).</summary>
    public int ClaimLeaseSeconds { get; set; } = 60;

    /// <summary>Failures after which the long cooldown applies instead of the short ones.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Wait after the first failures: attempt 1, then attempt 2 (and anything below <see cref="MaxAttempts"/>).</summary>
    public int ShortCooldownSeconds { get; set; } = 30;

    /// <summary>Wait after <see cref="MaxAttempts"/> failures for the same text; also used for permanent errors.</summary>
    public int LongCooldownMinutes { get; set; } = 360;

    /// <summary>Wait before re-checking after the monthly budget refused a request.</summary>
    public int QuotaCooldownMinutes { get; set; } = 10;

    /// <summary>Bump to invalidate every stored translation (e.g. a provider or glossary change).</summary>
    public int PolicyVersion { get; set; } = 1;
}
