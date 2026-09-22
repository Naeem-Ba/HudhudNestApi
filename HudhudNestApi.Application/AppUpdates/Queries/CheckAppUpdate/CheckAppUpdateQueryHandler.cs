using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.AppUpdates.DTOs;
using HudhudNestApi.Application.AppUpdates.Interfaces;
using HudhudNestApi.Domain.AppUpdates.ValueObjects;

namespace HudhudNestApi.Application.AppUpdates.Queries.CheckAppUpdate;

/// <summary>
/// The core update-decision logic. Must never throw or break app startup: on top of
/// IAppReleaseCacheService's own cache/DB fallback, this handler wraps that call in its own
/// catch-all and returns the safe "no update available" result rather than propagate — a
/// second, independent layer of defense on the hottest path in the feature.
/// </summary>
public sealed class CheckAppUpdateQueryHandler : IRequestHandler<CheckAppUpdateQuery, AppUpdateCheckResultDto>
{
    private static readonly AppUpdateCheckResultDto NoUpdateAvailable = new(
        UpdateAvailable: false,
        Mandatory: false,
        LatestVersion: null,
        MinimumSupportedVersion: null,
        StoreUrl: null,
        ReleaseNotes: null,
        ReleaseDate: null);

    private readonly IAppReleaseCacheService _cache;
    private readonly ILogger<CheckAppUpdateQueryHandler> _logger;

    public CheckAppUpdateQueryHandler(IAppReleaseCacheService cache, ILogger<CheckAppUpdateQueryHandler> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<AppUpdateCheckResultDto> Handle(CheckAppUpdateQuery request, CancellationToken ct)
    {
        EffectiveAppReleaseDto? effective;
        try
        {
            effective = await _cache.GetEffectiveReleaseAsync(request.Platform, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resolve the effective app release for {Platform}. Reporting no update available.",
                request.Platform);
            return NoUpdateAvailable;
        }

        if (effective is null)
            return NoUpdateAvailable;

        // The validator guarantees this parses; AppVersion.Parse is the throwing variant used
        // only for values already known to be valid.
        var current = AppVersion.Parse(request.CurrentVersion);
        var latest = AppVersion.Parse(effective.Version);
        var minimumSupported = AppVersion.Parse(effective.MinimumSupportedVersion);

        return new AppUpdateCheckResultDto(
            UpdateAvailable: current < latest,
            // Deliberately compared against the minimum-supported floor, not against latest —
            // a newer version existing never forces anything by itself.
            Mandatory: current < minimumSupported,
            LatestVersion: effective.Version,
            MinimumSupportedVersion: effective.MinimumSupportedVersion,
            StoreUrl: effective.StoreUrl,
            ReleaseNotes: SelectReleaseNotes(effective, request.Language),
            ReleaseDate: effective.ReleaseDate);
    }

    private static string? SelectReleaseNotes(EffectiveAppReleaseDto release, string? language) => language switch
    {
        "ar" => release.ReleaseNotesAr,
        "de" => release.ReleaseNotesDe,
        _ => release.ReleaseNotesEn,
    };
}
