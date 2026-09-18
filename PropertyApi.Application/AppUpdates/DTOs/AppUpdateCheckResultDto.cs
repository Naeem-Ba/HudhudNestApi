namespace PropertyApi.Application.AppUpdates.DTOs;

/// <summary>
/// Response of the public "check for update" endpoint. When no enabled release exists yet for
/// the requested platform, this is <c>UpdateAvailable=false, Mandatory=false</c> with every
/// other field null — never an error, never 404.
/// </summary>
public sealed record AppUpdateCheckResultDto(
    bool UpdateAvailable,
    bool Mandatory,
    string? LatestVersion,
    string? MinimumSupportedVersion,
    string? StoreUrl,
    string? ReleaseNotes,
    DateTime? ReleaseDate);
