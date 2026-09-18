namespace PropertyApi.Domain.AppUpdates.Enums;

/// <summary>
/// The client platform an <see cref="Entities.AppRelease"/> applies to. Stored as a string in
/// the database (see AppReleaseConfiguration) — adding a platform later is just a new member
/// plus a migration, never a schema redesign.
/// </summary>
public enum AppPlatform
{
    Android = 0,
    IOS = 1,
    Web = 2,
}
