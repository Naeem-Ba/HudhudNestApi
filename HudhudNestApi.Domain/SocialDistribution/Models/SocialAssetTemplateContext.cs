using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Domain.SocialDistribution.Models;

/// <summary>
/// Everything <see cref="Templates.SocialAssetTemplateRenderer"/> needs to deterministically
/// render one asset (Phase 7 spec §17) — a plain, framework-free data bag. Every text field is
/// rendered as literal text (XML-escaped by the renderer) — never markup, never evaluated as a
/// template string, so there is no injection surface (spec §27: "امنع XSS داخل النصوص والقوالب").
/// </summary>
public sealed record SocialAssetTemplateContext(
    Guid PropertyId,
    string TemplateId,
    int TemplateVersion,
    SocialPlatform Platform,
    SocialAssetType AssetType,
    string Language,
    string Title,
    string? Location,
    string? PriceText,
    string? AreaText,
    string? RoomsText,
    string? PropertyTypeLabel,
    string? TransactionTypeLabel,
    string? SourceImageUrl,
    BrandIdentity Brand)
{
    public bool IsRightToLeft => string.Equals(Language, "ar", StringComparison.OrdinalIgnoreCase);
}
