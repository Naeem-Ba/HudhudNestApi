namespace PropertyApi.Infrastructure.SocialDistribution.Assets;

/// <summary>
/// Configuration-bound brand identity (Phase 7 spec §24) — section <c>SocialDistribution:Brand</c>.
/// Defaults match HudhudNest's navy/gold identity (see the frontend landing page) so the
/// template engine produces on-brand output even with no configuration present.
/// </summary>
public sealed class BrandOptions
{
    public const string SectionName = "SocialDistribution:Brand";

    public string Name { get; set; } = "هدهد نيست";

    public string LogoUrl { get; set; } = string.Empty;

    public string PrimaryColor { get; set; } = "#0B3D59";

    public string SecondaryColor { get; set; } = "#D4AF37";

    public string TextColor { get; set; } = "#FFFFFF";

    public string BackgroundColor { get; set; } = "#0B3D59";

    public string FontFamily { get; set; } = "Arial, 'Noto Sans Arabic', sans-serif";

    /// <summary>Bumped when this configuration changes in a way that should be traceable on assets generated under it — see BrandIdentity's remarks.</summary>
    public int Version { get; set; } = 1;
}
