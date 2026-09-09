using System.Text.RegularExpressions;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Domain.SocialDistribution.Policies;

/// <summary>
/// Pure, platform-aware validation/sanitization rules for <see cref="Entities.SocialPostContent"/>
/// (spec §7/§18). Centralized here — exactly one place — so a new platform's limits are added by
/// extending <see cref="LimitsByPlatform"/>, never by touching <see cref="Entities.SocialPostContent"/>
/// itself.
///
/// Every method here fails safe: an out-of-policy value is either capped/dropped, never silently
/// truncated into a different-but-plausible value that could misreport analytics, and never
/// throws for a single bad hashtag (only the content's Title/Body/TargetUrl/Language are hard
/// requirements — see <see cref="Entities.SocialPostContent.Create"/> for which is which).
/// </summary>
public static class SocialContentPolicy
{
    public sealed record PlatformLimits(int MaxTitleLength, int MaxBodyLength, int MaxHashtags);

    /// <summary>
    /// Realistic, conservative caps per platform (well under each platform's actual technical
    /// ceiling) — generous enough for a real listing description, tight enough that a caption
    /// isn't silently cut off by the platform itself after AqarTech already "successfully"
    /// queued it.
    /// </summary>
    private static readonly Dictionary<SocialPlatform, PlatformLimits> LimitsByPlatform = new()
    {
        [SocialPlatform.Facebook] = new PlatformLimits(MaxTitleLength: 100, MaxBodyLength: 5000, MaxHashtags: 10),
        [SocialPlatform.Instagram] = new PlatformLimits(MaxTitleLength: 100, MaxBodyLength: 2200, MaxHashtags: 30),
        [SocialPlatform.Telegram] = new PlatformLimits(MaxTitleLength: 100, MaxBodyLength: 4096, MaxHashtags: 15),
        [SocialPlatform.TikTok] = new PlatformLimits(MaxTitleLength: 100, MaxBodyLength: 2200, MaxHashtags: 10),
        [SocialPlatform.YouTube] = new PlatformLimits(MaxTitleLength: 100, MaxBodyLength: 5000, MaxHashtags: 15),
        [SocialPlatform.LinkedIn] = new PlatformLimits(MaxTitleLength: 150, MaxBodyLength: 3000, MaxHashtags: 10),
    };

    private const int MaxHashtagLength = 30;
    private const int MaxLanguageLength = 5;

    private static readonly Regex HtmlTagPattern = new("<[^>]*>", RegexOptions.Compiled);

    /// <summary>
    /// Strips ASCII control characters (U+0000-U+001F, U+007F) but keeps tab/newline/
    /// carriage-return — a caption is allowed line breaks, just not embedded control bytes.
    /// </summary>
    private static readonly Regex ControlCharPattern =
        new(@"[\p{Cc}-[\t\n\r]]", RegexOptions.Compiled);

    private static readonly Regex HashtagPattern = new(@"^[\p{L}\p{N}_]{1,30}$", RegexOptions.Compiled);
    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase) { "ar", "en" };

    public static PlatformLimits GetLimits(SocialPlatform platform) =>
        LimitsByPlatform.TryGetValue(platform, out var limits)
            ? limits
            : new PlatformLimits(MaxTitleLength: 100, MaxBodyLength: 2000, MaxHashtags: 10);

    /// <summary>Strips HTML tags and control characters, then trims. Never throws.</summary>
    public static string SanitizePlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var withoutTags = HtmlTagPattern.Replace(value, string.Empty);
        var withoutControlChars = ControlCharPattern.Replace(withoutTags, string.Empty);
        return withoutControlChars.Trim();
    }

    /// <summary>
    /// Validates and normalizes a hashtag list: strips a leading '#', drops anything that fails
    /// <see cref="HashtagPattern"/> (spaces, punctuation, scripts — never rejects the whole
    /// content for one bad tag), de-duplicates case-insensitively, and caps the count per the
    /// platform's limit. Returns tags WITHOUT a leading '#' — rendering it is a presentation
    /// concern for whichever provider actually posts the content.
    /// </summary>
    public static IReadOnlyList<string> NormalizeHashtags(IEnumerable<string>? hashtags, SocialPlatform platform)
    {
        if (hashtags is null)
            return Array.Empty<string>();

        var limits = GetLimits(platform);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var raw in hashtags)
        {
            if (result.Count >= limits.MaxHashtags)
                break;

            var candidate = raw?.Trim().TrimStart('#') ?? string.Empty;

            if (candidate.Length == 0 || candidate.Length > MaxHashtagLength)
                continue;

            if (!HashtagPattern.IsMatch(candidate))
                continue;

            if (seen.Add(candidate))
                result.Add(candidate);
        }

        return result;
    }

    public static bool IsSupportedLanguage(string? language) =>
        !string.IsNullOrWhiteSpace(language) &&
        language.Length <= MaxLanguageLength &&
        SupportedLanguages.Contains(language);

    public static string NormalizeLanguage(string language) => language.Trim().ToLowerInvariant();

    /// <summary>
    /// A TargetUrl must be an absolute, public http(s) link — never an internal SPA route
    /// (spec §18: "يجب ألا يحتوي TargetUrl على Internal Route") and never a non-http(s) scheme
    /// a malicious/garbage value could smuggle in (javascript:, data:, file:, ...).
    /// </summary>
    public static bool IsValidPublicUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
        (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    public static bool FitsTitleLimit(string title, SocialPlatform platform) =>
        title.Length <= GetLimits(platform).MaxTitleLength;

    public static bool FitsBodyLimit(string body, SocialPlatform platform) =>
        body.Length <= GetLimits(platform).MaxBodyLength;
}
