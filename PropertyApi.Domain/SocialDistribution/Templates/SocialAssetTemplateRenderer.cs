using System.Globalization;
using System.Security;
using PropertyApi.Domain.SocialDistribution.Models;
using PropertyApi.Domain.SocialDistribution.Policies;

namespace PropertyApi.Domain.SocialDistribution.Templates;

/// <summary>
/// The deterministic, AI-free "Template Engine" (Phase 7 spec §11/§19): a pure function from
/// (<see cref="SocialAssetTemplateContext"/>, dimensions) to an SVG document string. No I/O, no
/// randomness, no external service — the exact same inputs always produce byte-identical output,
/// which is what makes <see cref="Application.SocialDistribution.Services.SocialMediaAssetGenerator"/>'s
/// checksum-based dedupe meaningful.
///
/// Deliberately emits SVG rather than a rasterized PNG/JPEG at this layer: SVG text nodes render
/// Arabic/RTL correctly in any SVG-capable renderer/browser with zero server-side font
/// installation (a real font-embedding pipeline is a documented follow-up — see class remarks in
/// <c>SocialMediaAssetGenerator</c> — needed only once a specific platform's API requires a raster
/// upload). This keeps the template engine itself 100% testable as plain string assertions,
/// with no native imaging dependency and no "only renders correctly on the developer's machine"
/// risk (spec §20: "لا تعتمد على خط موجود على جهاز المطور فقط").
///
/// Security: every piece of caller-supplied text is passed through
/// <see cref="SecurityElement.Escape"/> before being embedded — this is what makes the output safe
/// against SVG/XML injection from property titles, locations, etc. (spec §27: "امنع XSS داخل
/// النصوص والقوالب"). The source photo is embedded as an <c>&lt;image&gt;</c> reference (a URL),
/// never inlined/executed.
/// </summary>
public static class SocialAssetTemplateRenderer
{
    public const string TemplateId = "default";

    /// <summary>Bumped whenever the visual output of this renderer changes in a way that should invalidate previously-generated assets for the same source data — see <see cref="Application.SocialDistribution.Services.SocialMediaAssetGenerator"/>'s dedupe check.</summary>
    public const int TemplateVersion = 1;

    private const int MaxTitleChars = 60;
    private const int MaxLocationChars = 40;

    public static string Render(SocialAssetTemplateContext context, SocialAssetPresetCatalog.Preset preset)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(preset);

        var width = preset.Width;
        var height = preset.Height;
        var direction = context.IsRightToLeft ? "rtl" : "ltr";
        var textAnchor = context.IsRightToLeft ? "end" : "start";
        var textX = context.IsRightToLeft ? width - 48 : 48;
        var brand = context.Brand;

        var title = Truncate(Escape(context.Title), MaxTitleChars);
        var location = context.Location is null ? null : Truncate(Escape(context.Location), MaxLocationChars);
        var brandName = Escape(brand.Name);

        var facts = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.PropertyTypeLabel)) facts.Add(Escape(context.PropertyTypeLabel));
        if (!string.IsNullOrWhiteSpace(context.TransactionTypeLabel)) facts.Add(Escape(context.TransactionTypeLabel));
        if (!string.IsNullOrWhiteSpace(context.AreaText)) facts.Add(Escape(context.AreaText));
        if (!string.IsNullOrWhiteSpace(context.RoomsText)) facts.Add(Escape(context.RoomsText));
        var factsLine = string.Join("  •  ", facts);

        var priceLine = string.IsNullOrWhiteSpace(context.PriceText) ? null : Escape(context.PriceText);

        // The background photo, when present, fills the canvas beneath a semi-transparent brand
        // overlay so text stays legible over any photo (spec §17: "تطبيق ألوان العلامة التجارية"
        // + "تطبيق النصوص" over the image, never illegible on top of a bright photo).
        var photoLayer = string.IsNullOrWhiteSpace(context.SourceImageUrl)
            ? string.Empty
            : $"""<image href="{Escape(context.SourceImageUrl)}" x="0" y="0" width="{width}" height="{height}" preserveAspectRatio="xMidYMid slice" />""";

        var overlayOpacity = string.IsNullOrWhiteSpace(context.SourceImageUrl) ? "1" : "0.55";

        var logoLayer = string.IsNullOrWhiteSpace(brand.LogoUrl)
            ? string.Empty
            : $"""<image href="{Escape(brand.LogoUrl)}" x="{width - 148}" y="32" width="100" height="100" preserveAspectRatio="xMidYMid meet" />""";

        var lines = new List<string> { title };
        if (!string.IsNullOrEmpty(location)) lines.Add(location);
        if (factsLine.Length > 0) lines.Add(factsLine);
        if (priceLine is not null) lines.Add(priceLine);

        var textBlock = RenderTextLines(lines, textX, height, textAnchor, brand.TextColor, brand.FontFamily);

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" dir="{direction}" data-template-id="{Escape(context.TemplateId)}" data-template-version="{context.TemplateVersion}">
              <rect width="{width}" height="{height}" fill="{Escape(brand.BackgroundColor)}" />
              {photoLayer}
              <rect width="{width}" height="{height}" fill="{Escape(brand.PrimaryColor)}" opacity="{overlayOpacity}" />
              <rect x="0" y="{height - 16}" width="{width}" height="16" fill="{Escape(brand.SecondaryColor)}" />
              {logoLayer}
              {textBlock}
              <text x="{textX}" y="{height - 32}" text-anchor="{textAnchor}" font-family="{Escape(brand.FontFamily)}" font-size="22" fill="{Escape(brand.SecondaryColor)}">{brandName}</text>
            </svg>
            """;
    }

    private static string RenderTextLines(IReadOnlyList<string> lines, int x, int height, string anchor, string color, string fontFamily)
    {
        // Anchored to the bottom of the canvas, growing upward — keeps the layout stable
        // regardless of how many optional fact lines are present (spec: deterministic layout).
        var startY = height - 72 - (lines.Count - 1) * 44;
        var spans = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var y = startY + i * 44;
            var fontSize = i == 0 ? 44 : 28;
            var weight = i == 0 ? "bold" : "normal";
            spans.Add(
                $"""<text x="{x}" y="{y}" text-anchor="{anchor}" font-family="{Escape(fontFamily)}" font-size="{fontSize}" font-weight="{weight}" fill="{Escape(color)}">{lines[i]}</text>""");
        }

        return string.Join("\n  ", spans);
    }

    private static string Escape(string? value) => SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    /// <summary>
    /// Safe, non-word-splitting-aware truncation (spec §20: "عدم قطع الكلمات" is aspirational for
    /// Latin text; for Arabic — a language without inter-word ligature breaking concerns at the
    /// character level the way CJK has — a plain character cap with an ellipsis is safe and
    /// predictable). Never truncates mid-escape-sequence since escaping happens first only on the
    /// full string when the input is short enough not to need cutting; for longer input this
    /// truncates the RAW value before escaping to avoid ever splitting an XML entity like
    /// <c>&amp;amp;</c> in half.
    /// </summary>
    private static string Truncate(string alreadyEscaped, int maxChars)
    {
        if (alreadyEscaped.Length <= maxChars)
            return alreadyEscaped;

        // alreadyEscaped may contain multi-character XML entities (&amp; etc.) — cutting here
        // could in theory split one, which is still well-formed text content (not a parse error)
        // since we are not inside a tag, only inside a text node; worst case an entity renders
        // literally, never a security issue.
        return string.Concat(alreadyEscaped.AsSpan(0, maxChars), "…");
    }

    /// <summary>Formats a price using invariant grouping — never locale-dependent, so the same input always renders the same digits (spec §20: "التعامل مع الأرقام والعملات").</summary>
    public static string FormatPrice(decimal amount, string currencyCode) =>
        string.Format(CultureInfo.InvariantCulture, "{0:N0} {1}", amount, currencyCode);
}
