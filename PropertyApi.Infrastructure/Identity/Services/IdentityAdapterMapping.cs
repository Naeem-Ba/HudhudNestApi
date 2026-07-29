using Microsoft.AspNetCore.Identity;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Identity.Services;

internal static class IdentityAdapterMapping
{
    public static IdentityAccountSnapshot Snapshot(ApplicationUser user) =>
        new(
            user.Id,
            user.Id,
            user.Email,
            user.PhoneNumber,
            user.EmailConfirmed,
            user.PhoneNumberConfirmed,
            !string.IsNullOrWhiteSpace(user.PasswordHash),
            user.IsDeleted,
            user.UserName,
            user.SecurityStamp,
            user.PhoneLastVerifiedAtUtc,
            user.PhoneVerificationDueAtUtc,
            user.PhoneVerificationGraceEndsAtUtc,
            user.PhoneVerificationState,
            user.IsBanned,
            user.LockoutEnd);

    public static IdentityOperationResult Result(IdentityResult result) =>
        result.Succeeded
            ? IdentityOperationResult.Success()
            : IdentityOperationResult.Failed(result.Errors.Select(error => error.Description));

    public static async Task<ApplicationUser> RequireAsync(
        UserManager<ApplicationUser> users,
        Guid identityId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await users.FindByIdAsync(identityId.ToString())
            ?? throw new InvalidOperationException($"Identity user '{identityId}' was not found.");
    }
}
