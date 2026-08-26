namespace PropertyApi.Application.Plans.DTOs;

/// <summary>
/// Public plan-catalog shape, matching FRONTEND_BACKEND_CONTRACT.md §11.2 exactly.
/// Content fields are i18n keys, not display text — see Plan's doc comment.
/// </summary>
public sealed class PlanDto
{
    public string Tier { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;
    public string TaglineKey { get; set; } = string.Empty;
    public string PriceKind { get; set; } = string.Empty;
    public decimal? PriceUsd { get; set; }
    public string[] FeatureKeys { get; set; } = Array.Empty<string>();
    public string? NoteKey { get; set; }
    public bool IsRecommended { get; set; }
    public string CtaKey { get; set; } = string.Empty;
}
