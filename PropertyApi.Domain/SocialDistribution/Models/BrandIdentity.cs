namespace PropertyApi.Domain.SocialDistribution.Models;

/// <summary>
/// HudhudNest's visual identity, as needed by the template engine (Phase 7 spec §24). Sourced from
/// configuration in exactly one place (<c>BrandOptions</c>, Infrastructure) and passed down —
/// never hardcoded a second time inside a renderer or generator. <see cref="Version"/> lets a
/// future brand refresh be tracked without breaking the meaning of an already-generated asset's
/// stored <c>TemplateVersion</c> (spec: "عند تغيير Brand Version، لا تكسر Assets المنشورة سابقاً").
/// </summary>
public sealed record BrandIdentity(
    string Name,
    string LogoUrl,
    string PrimaryColor,
    string SecondaryColor,
    string TextColor,
    string BackgroundColor,
    string FontFamily,
    int Version);
