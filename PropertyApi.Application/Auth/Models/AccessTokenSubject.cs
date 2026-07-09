namespace PropertyApi.Application.Auth.Models;

/// <summary>
/// Framework-neutral data required to issue an access token.
///
/// This contract deliberately avoids depending on:
/// - ASP.NET Core Identity types
/// - Infrastructure.ApplicationUser
///
/// During the migration period, IdentityId is populated from legacy User.Id.
/// The subject identifier corresponds to the authenticated identity account ID.
/// </summary>
public sealed record AccessTokenSubject(
    Guid IdentityId,
    string? Email,
    string? UserName,
    string? SecurityStamp);