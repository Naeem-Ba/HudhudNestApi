namespace PropertyApi.Application.Users.Models;

/// <summary>
/// Lightweight user-directory projection for application workflows
/// that only need active-user identity and display information.
/// </summary>
public sealed record UserDirectoryEntry(
    Guid UserId,
    string DisplayName,
    string? ProfileImageUrl);