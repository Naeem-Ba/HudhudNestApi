namespace HudhudNestApi.Application.AppUpdates.DTOs;

/// <summary>
/// Plain, JSON-round-trippable projection of the effective release for a platform. Deliberately
/// not the AppRelease domain entity itself: AppRelease has a private constructor and private
/// setters (DDD convention throughout this codebase), which System.Text.Json cannot deserialize
/// — DistributedCacheExtensions.GetOrCreateAsync always JSON-round-trips its value regardless of
/// which IDistributedCache backing store is used, so anything cached through it must be a plain
/// DTO like this one (matching how CommonLookupService only ever caches DTOs, never entities).
/// </summary>
public sealed record EffectiveAppReleaseDto(
    string Version,
    string MinimumSupportedVersion,
    string? StoreUrl,
    string? ReleaseNotesAr,
    string? ReleaseNotesEn,
    string? ReleaseNotesDe,
    DateTime ReleaseDate);
