using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.AiContent;

/// <summary>
/// Current, production <see cref="ISocialContentGenerator"/> implementation (Phase 8 spec §3-§4):
/// deterministic, rule-based, zero network calls, zero API keys. Every fact it writes comes
/// straight from <see cref="PropertySocialFacts"/> — it never rounds a price to a "nicer" number,
/// never invents an amenity, and never adds a claim ("nicest in the city!") that isn't derivable
/// from the supplied facts, which is exactly what makes <see cref="RequiresReview"/> always
/// <c>false</c> here (see interface docs for the future-AI extension point this leaves open).
/// </summary>
public sealed class TemplateSocialContentGenerator : ISocialContentGenerator
{
    public Task<GeneratedSocialContent> GenerateAsync(GenerateSocialContentRequest request, CancellationToken ct = default)
    {
        var facts = request.Facts;
        var isArabic = string.Equals(request.Language, "ar", StringComparison.OrdinalIgnoreCase);

        var factLine = BuildFactLine(facts, isArabic);
        var intro = BuildIntro(facts, isArabic);
        var cta = BuildCallToAction(isArabic);
        var linkLine = string.Format(
            CultureInfo.InvariantCulture,
            isArabic ? "التفاصيل والصور: {0}" : "Details & photos: {0}",
            facts.CanonicalUrl);

        var body = request.Platform switch
        {
            SocialPlatform.Instagram => string.Join('\n', new[] { intro, factLine, cta }.Where(s => s.Length > 0)),
            SocialPlatform.Telegram => string.Join('\n', new[] { intro, factLine, linkLine }.Where(s => s.Length > 0)),
            _ => string.Join('\n', new[] { intro, factLine, cta, linkLine }.Where(s => s.Length > 0)),
        };

        var hashtags = BuildHashtags(facts, isArabic);

        return Task.FromResult(new GeneratedSocialContent(
            Platform: request.Platform,
            Title: facts.Title, // never re-worded: the title is a Domain fact, not something this generator is allowed to rephrase.
            Body: body,
            Caption: request.Platform == SocialPlatform.Instagram ? body : null,
            Hashtags: hashtags,
            CallToAction: cta,
            Language: request.Language,
            SourceFactsHash: ComputeFactsHash(facts),
            RequiresReview: false));
    }

    private static string BuildIntro(PropertySocialFacts facts, bool isArabic)
    {
        var transaction = NormalizeTransaction(facts.TransactionType, isArabic);
        var type = string.IsNullOrWhiteSpace(facts.PropertyType) ? (isArabic ? "عقار" : "property") : facts.PropertyType;

        return isArabic
            ? $"{type} {transaction} في {(string.IsNullOrWhiteSpace(facts.City) ? facts.Province : facts.City) ?? "سوريا"}"
            : $"{type} {transaction} in {facts.City ?? facts.Province ?? "Syria"}";
    }

    private static string NormalizeTransaction(string transactionType, bool isArabic)
    {
        var forSale = transactionType.Contains("Sale", StringComparison.OrdinalIgnoreCase) ||
                      transactionType.Contains("بيع", StringComparison.OrdinalIgnoreCase);

        return isArabic ? (forSale ? "للبيع" : "للإيجار") : (forSale ? "for sale" : "for rent");
    }

    private static string BuildFactLine(PropertySocialFacts facts, bool isArabic)
    {
        var parts = new List<string>();

        if (facts.Area is > 0)
            parts.Add(isArabic ? $"{FormatNumber(facts.Area.Value)} م²" : $"{FormatNumber(facts.Area.Value)} m²");

        if (facts.Rooms is > 0)
            parts.Add(isArabic ? $"{facts.Rooms} غرف" : $"{facts.Rooms} rooms");

        if (facts.Bathrooms is > 0)
            parts.Add(isArabic ? $"{facts.Bathrooms} حمامات" : $"{facts.Bathrooms} baths");

        if (facts.Price is > 0)
        {
            var currency = string.IsNullOrWhiteSpace(facts.Currency) ? string.Empty : $" {facts.Currency}";
            // Only for an unambiguous rent-only listing — a mixed ForRentAndSale's Price could be
            // either figure (BuildFacts prefers PurchasePrice), so it is never labeled here.
            var period = string.Equals(facts.TransactionType, "ForRent", StringComparison.OrdinalIgnoreCase)
                ? (isArabic ? " شهرياً" : "/mo")
                : string.Empty;
            parts.Add($"{FormatNumber(facts.Price.Value)}{currency}{period}");
        }

        return string.Join(" - ", parts);
    }

    private static string FormatNumber(decimal value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string BuildCallToAction(bool isArabic) =>
        isArabic ? "للاستفسار والمعاينة تواصل معنا الآن" : "Contact us now to inquire or schedule a viewing";

    private static IReadOnlyList<string> BuildHashtags(PropertySocialFacts facts, bool isArabic)
    {
        var tags = new List<string>();

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                tags.Add(value.Replace(" ", string.Empty));
        }

        Add(isArabic ? "عقارات" : "RealEstate");
        Add(isArabic ? "عقار_تيك" : "AqarTech");
        Add(facts.City);
        Add(facts.Province);
        Add(facts.PropertyType);

        return tags;
    }

    /// <summary>
    /// Deterministic fingerprint of the facts a piece of content was generated from — lets a
    /// caller detect "these facts changed since this content was generated" without re-diffing
    /// every field (spec §3 output shape: "sourceFactsHash"). SHA-256 over a stable, delimiter-
    /// separated projection of every fact field; invariant-culture formatted so the hash never
    /// varies with the host's current culture.
    /// </summary>
    private static string ComputeFactsHash(PropertySocialFacts facts)
    {
        var canonical = string.Join('|', new[]
        {
            facts.PropertyId.ToString(),
            facts.Title,
            facts.PropertyType,
            facts.TransactionType,
            facts.Province,
            facts.City,
            facts.Address,
            facts.Price?.ToString(CultureInfo.InvariantCulture),
            facts.Currency,
            facts.Area?.ToString(CultureInfo.InvariantCulture),
            facts.Rooms?.ToString(CultureInfo.InvariantCulture),
            facts.Bathrooms?.ToString(CultureInfo.InvariantCulture),
            facts.Status,
            facts.CanonicalUrl,
        }.Select(s => s ?? string.Empty));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
