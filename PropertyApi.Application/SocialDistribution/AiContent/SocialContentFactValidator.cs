using System.Globalization;
using System.Text.RegularExpressions;
using PropertyApi.Domain.SocialDistribution.Models;
using PropertyApi.Domain.SocialDistribution.Policies;

namespace PropertyApi.Application.SocialDistribution.AiContent;

/// <summary>
/// Guards against exactly what Phase 8 spec §3 forbids: generated content that invents or
/// contradicts a property fact. Pure, static, no I/O — every <see cref="ISocialContentGenerator"/>
/// output (today's deterministic template AND any future AI-backed one) MUST pass through this
/// before <see cref="Commands.CreateSocialPublication.CreateSocialPublicationCommandHandler"/> will
/// use it; a failure falls back to the pre-existing minimal fact-line template rather than
/// publishing anything unverified.
///
/// This is deliberately conservative rather than exhaustive: it does not attempt full NLP fact
/// extraction (out of scope for a Ports-and-Adapters port with no AI dependency), only the checks
/// that are both mechanically reliable and directly named in the spec — a fabricated PRICE number,
/// a URL other than the property's own canonical link, and raw HTML/script markup.
/// </summary>
public static class SocialContentFactValidator
{
    private static readonly Regex UrlPattern = new(@"https?://[^\s]+", RegexOptions.Compiled);
    private static readonly Regex HtmlTagPattern = new("<[^>]*>", RegexOptions.Compiled);

    /// <summary>Matches a run of digits (with optional thousands separators) that could plausibly be a price figure.</summary>
    private static readonly Regex NumberPattern = new(@"\d[\d,\.]*\d|\d", RegexOptions.Compiled);

    public static SocialContentValidationResult Validate(GeneratedSocialContent content, PropertySocialFacts facts)
    {
        var errors = new List<string>();
        var combinedText = string.Join(' ', new[] { content.Title, content.Body, content.Caption }.Where(s => !string.IsNullOrEmpty(s)));

        // 1) No raw markup — content must be plain text, never HTML the platform (or our own
        // storage) could interpret as anything other than a caption (spec §"امنع HTML وXSS").
        if (HtmlTagPattern.IsMatch(combinedText))
            errors.Add("المحتوى المولّد يحتوي على HTML/Markup غير مسموح.");

        // 2) Every URL mentioned must be the property's own canonical link — never a link the
        // generator invented (spec §"رفض الروابط غير الصحيحة").
        foreach (Match match in UrlPattern.Matches(combinedText))
        {
            if (!string.Equals(match.Value.TrimEnd('.', ')', ']'), facts.CanonicalUrl, StringComparison.Ordinal))
                errors.Add($"المحتوى المولّد يحتوي على رابط غير معروف: {match.Value}");
        }

        // 3) If a price is mentioned, it must be THE property's real price (spec §"رفض السعر
        // المختلق") — never a plausible-looking but different number the generator invented.
        if (facts.Price is > 0)
        {
            var realPrice = Math.Round(facts.Price.Value, 0, MidpointRounding.AwayFromZero);
            var mentionsAnyNumberNearPriceMagnitude = false;
            var mentionsRealPrice = false;

            // Links are checked separately above — the digits inside a property link (its GUID id,
            // the publication id in utm_content) are never a price claim, so scan the prose only.
            // Without this, any GUID with 4+ consecutive digits made every link-bearing body
            // (Facebook, Telegram) look like it quoted a wrong price.
            var textWithoutUrls = UrlPattern.Replace(combinedText, " ");

            foreach (Match match in NumberPattern.Matches(textWithoutUrls))
            {
                var digitsOnly = match.Value.Replace(",", string.Empty).Replace(".", string.Empty);
                if (!decimal.TryParse(digitsOnly, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    continue;

                // Ignore small numbers (room/bathroom counts) — only compare figures large enough to plausibly be a price.
                if (parsed < 1000)
                    continue;

                mentionsAnyNumberNearPriceMagnitude = true;
                if (parsed == realPrice)
                    mentionsRealPrice = true;
            }

            if (mentionsAnyNumberNearPriceMagnitude && !mentionsRealPrice)
                errors.Add("المحتوى المولّد يذكر سعراً لا يطابق سعر العقار الفعلي.");
        }

        // 4) Platform length limits — checked against the SAME policy the Domain entity itself
        // enforces, so a rejection here means Create()/Revise() would have thrown too; catching
        // it here lets the caller fall back to the safe template instead of a DomainException.
        var limits = SocialContentPolicy.GetLimits(content.Platform);
        if (content.Body.Length > limits.MaxBodyLength)
            errors.Add($"نص المنشور المولّد يتجاوز الحد المسموح لهذه المنصة ({limits.MaxBodyLength} حرفاً).");

        if (!string.IsNullOrEmpty(content.Title) && content.Title.Length > limits.MaxTitleLength)
            errors.Add($"عنوان المنشور المولّد يتجاوز الحد المسموح لهذه المنصة ({limits.MaxTitleLength} حرفاً).");

        return errors.Count == 0 ? SocialContentValidationResult.Valid : SocialContentValidationResult.Invalid(errors.ToArray());
    }
}
