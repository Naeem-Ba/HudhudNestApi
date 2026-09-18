using PropertyApi.Domain.AppUpdates.Enums;
using PropertyApi.Domain.AppUpdates.ValueObjects;
using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;

namespace PropertyApi.Domain.AppUpdates.Entities;

/// <summary>
/// A published app version for one platform, plus the "minimum supported version" policy in
/// effect as of that release. DDD: private setters + factory + explicit behavior methods, same
/// shape as InvestmentProject/ShortStayListing.
///
/// Deliberately has NO "IsMandatory" column. Whether a given client must update is derived at
/// read time (client version &lt; the current effective release's MinimumSupportedVersion),
/// never stored — a release merely existing with a higher Version never forces anything by
/// itself; only falling below the minimum-supported floor does. Conflating "a newer version
/// exists" with "your version is no longer supported" was called out as the one mistake this
/// feature must not make, so resist the urge to add a manual mandatory flag back in.
/// </summary>
public sealed class AppRelease : AuditableEntity
{
    public AppPlatform Platform { get; private set; }

    /// <summary>Canonical "major.minor.patch" form of the parsed <see cref="AppVersion"/>.</summary>
    public string Version { get; private set; } = string.Empty;

    /// <summary>The minimum client version still allowed to keep working, as of this release.</summary>
    public string MinimumSupportedVersion { get; private set; } = string.Empty;

    /// <summary>Nullable by design — no real Google Play / App Store listing exists yet.</summary>
    public string? StoreUrl { get; private set; }

    public string? ReleaseNotesAr { get; private set; }
    public string? ReleaseNotesEn { get; private set; }
    public string? ReleaseNotesDe { get; private set; }

    public DateTime ReleaseDate { get; private set; }

    public bool IsEnabled { get; private set; }

    private AppRelease() { }

    public static AppRelease Create(
        AppPlatform platform,
        AppVersion version,
        AppVersion minimumSupportedVersion,
        string? storeUrl,
        string? releaseNotesAr,
        string? releaseNotesEn,
        string? releaseNotesDe,
        DateTime releaseDate,
        bool isEnabled)
    {
        EnsureMinimumNotAboveVersion(minimumSupportedVersion, version);

        return new AppRelease
        {
            Platform = platform,
            Version = version.ToString(),
            MinimumSupportedVersion = minimumSupportedVersion.ToString(),
            StoreUrl = NormalizeOrNull(storeUrl),
            ReleaseNotesAr = NormalizeOrNull(releaseNotesAr),
            ReleaseNotesEn = NormalizeOrNull(releaseNotesEn),
            ReleaseNotesDe = NormalizeOrNull(releaseNotesDe),
            ReleaseDate = releaseDate,
            IsEnabled = isEnabled,
        };
    }

    /// <summary>Platform is intentionally not editable here — changing platform is really
    /// "create a new row for a different platform," not an edit of this one.</summary>
    public void UpdateContent(
        AppVersion version,
        AppVersion minimumSupportedVersion,
        string? storeUrl,
        string? releaseNotesAr,
        string? releaseNotesEn,
        string? releaseNotesDe,
        DateTime releaseDate)
    {
        EnsureMinimumNotAboveVersion(minimumSupportedVersion, version);

        Version = version.ToString();
        MinimumSupportedVersion = minimumSupportedVersion.ToString();
        StoreUrl = NormalizeOrNull(storeUrl);
        ReleaseNotesAr = NormalizeOrNull(releaseNotesAr);
        ReleaseNotesEn = NormalizeOrNull(releaseNotesEn);
        ReleaseNotesDe = NormalizeOrNull(releaseNotesDe);
        ReleaseDate = releaseDate;
    }

    public void Enable() => IsEnabled = true;

    public void Disable() => IsEnabled = false;

    private static void EnsureMinimumNotAboveVersion(AppVersion minimumSupportedVersion, AppVersion version)
    {
        if (minimumSupportedVersion > version)
            throw new DomainException("الحد الأدنى للإصدار المدعوم لا يمكن أن يكون أحدث من الإصدار نفسه.");
    }

    private static string? NormalizeOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
