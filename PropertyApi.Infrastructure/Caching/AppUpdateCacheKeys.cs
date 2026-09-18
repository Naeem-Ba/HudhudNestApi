using PropertyApi.Domain.AppUpdates.Enums;

namespace PropertyApi.Infrastructure.Caching;

/// <summary>
/// Separate from LookupCacheKeys.cs deliberately: this key is parametrized by platform, not a
/// fixed set, so it can't participate in that file's RefreshAllAsync-style "All" listing (same
/// reasoning LookupCacheKeys gives for excluding Districts/Neighborhoods).
/// </summary>
public static class AppUpdateCacheKeys
{
    private const string Prefix = "app-update:effective-release";

    public static string EffectiveRelease(AppPlatform platform) => $"{Prefix}:{platform}";
}
