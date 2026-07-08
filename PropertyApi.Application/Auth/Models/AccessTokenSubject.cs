namespace PropertyApi.Application.Auth.Models;

/// <summary>
/// Framework-neutral data required to issue an access token.
///
/// This contract deliberately avoids depending on:
/// - ASP.NET Core Identity types
/// - Infrastructure.ApplicationUser
/// - Domain.Users.Entities.User
///
/// During the migration period, IdentityId is populated from legacy User.Id.
/// After the final Identity cutover, it will be populated from ApplicationUser.Id.
/// </summary>
public sealed record AccessTokenSubject(
    Guid IdentityId,
    string? Email,
    string? UserName,
    string? SecurityStamp);