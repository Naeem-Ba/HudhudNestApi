using PropertyApi.Domain.AppUpdates.Enums;

namespace PropertyApi.Application.AppUpdates.DTOs;

/// <summary>Full admin projection of an AppRelease row — reused for both the list grid and the
/// detail view, since release notes are plain nullable strings, not large blobs.</summary>
public sealed record AppReleaseAdminDto(
    Guid Id,
    AppPlatform Platform,
    string Version,
    string MinimumSupportedVersion,
    string? StoreUrl,
    string? ReleaseNotesAr,
    string? ReleaseNotesEn,
    string? ReleaseNotesDe,
    DateTime ReleaseDate,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt);
