using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Identity.Services;

/// <summary>
/// Framework-neutral Identity adapter.
///
/// During the migration phase, this adapter internally uses the legacy
/// Domain User Identity entity.
///
/// Application handlers communicate only through neutral contracts:
///
/// - Guid identity identifiers
/// - IdentityAccountSnapshot
/// - CreateIdentityAccount
/// - IdentityOperationResult
///
/// The internal UserManager type can later be changed to ApplicationUser
/// without forcing Application handlers to depend on that concrete type.
/// </summary>
public sealed class PureIdentityService
    : IIdentityCapabilityAdapter
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPhoneNumberLookupHasher _phoneLookupHasher;

    public PureIdentityService(
         UserManager<ApplicationUser> userManager,
        IPhoneNumberLookupHasher phoneLookupHasher)
    {
        _userManager = userManager;
        _phoneLookupHasher = phoneLookupHasher;
    }

    public async Task<IdentityAccountSnapshot?>
        FindByIdAsync(
            Guid identityId,
            CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user =
            await _userManager.FindByIdAsync(
                identityId.ToString());

        return user is null
            ? null
            : Map(user);
    }

    public async Task<IdentityAccountSnapshot?>
        FindByEmailAsync(
            string email,
            CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            email);

        ct.ThrowIfCancellationRequested();

        var user =
            await _userManager.FindByEmailAsync(
                email.Trim());

        return user is null
            ? null
            : Map(user);
    }

    public async Task<IdentityAccountSnapshot?>
        FindByLoginAsync(
            string loginProvider,
            string providerKey,
            CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            loginProvider);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            providerKey);

        ct.ThrowIfCancellationRequested();

        var user =
            await _userManager.FindByLoginAsync(
                loginProvider,
                providerKey);

        return user is null
            ? null
            : Map(user);
    }

    public async Task<IdentityAccountSnapshot?>
        FindByPhoneNumberAsync(
            string phoneNumber,
            CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            phoneNumber);

        ct.ThrowIfCancellationRequested();

        var lookupHash =
            _phoneLookupHasher.Compute(
                phoneNumber);

        var user =
            await _userManager.Users
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.PhoneNumberLookupHash ==
                        lookupHash,
                    ct);

        return user is null
            ? null
            : Map(user);
    }

    public async Task<IdentityOperationResult>
        CreateAsync(
            CreateIdentityAccount request,
            CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        ct.ThrowIfCancellationRequested();

        var now =
            request.LegacyCreatedAtUtc
            ?? DateTime.UtcNow;

        var phoneLookupHash =
            string.IsNullOrWhiteSpace(
                request.PhoneNumber)
                ? null
                : _phoneLookupHasher.Compute(
                    request.PhoneNumber);

        var user =
            new ApplicationUser
            {
                Id =
                    request.UserAccountId,

                UserName =
                    request.Email
                    ?? request.PhoneNumber
                    ?? request.UserAccountId
                        .ToString("N"),

                Email =
                    request.Email,

                EmailConfirmed =
                    request.EmailConfirmed,

                PhoneNumber =
                    request.PhoneNumber,

                PhoneNumberLookupHash =
                    phoneLookupHash,

                PhoneNumberConfirmed =
                    request.PhoneConfirmed,

                CreatedAt =
                    now,

                UpdatedAt =
                    now
            };

        var result =
            string.IsNullOrWhiteSpace(
                request.Password)
                ? await _userManager.CreateAsync(
                    user)
                : await _userManager.CreateAsync(
                    user,
                    request.Password);

        return Map(result);
    }

    public async Task<IdentityOperationResult>
        AddLoginAsync(
            Guid identityId,
            string loginProvider,
            string providerKey,
            string providerDisplayName,
            CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            loginProvider);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            providerKey);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            providerDisplayName);

        var user =
            await RequireUserAsync(
                identityId,
                ct);

        var loginInfo =
            new UserLoginInfo(
                loginProvider,
                providerKey,
                providerDisplayName);

        var result =
            await _userManager.AddLoginAsync(
                user,
                loginInfo);

        return Map(result);
    }

    public async Task<bool>
        CheckPasswordAsync(
            Guid identityId,
            string password,
            CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            password);

        var user =
            await RequireUserAsync(
                identityId,
                ct);

        return await _userManager.CheckPasswordAsync(
            user,
            password);
    }

    public async Task<IReadOnlyList<string>>
        GetRolesAsync(
            Guid identityId,
            CancellationToken ct = default)
    {
        var user =
            await RequireUserAsync(
                identityId,
                ct);

        var roles =
            await _userManager.GetRolesAsync(
                user);

        return roles.ToArray();
    }

    public async Task<IdentityOperationResult>
        AddToRoleAsync(
            Guid identityId,
            string role,
            CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            role);

        var user =
            await RequireUserAsync(
                identityId,
                ct);

        var result =
            await _userManager.AddToRoleAsync(
                user,
                role);

        return Map(result);
    }

    public async Task<IdentityOperationResult>
        ConfirmPhoneNumberAsync(
            Guid identityId,
            DateTime confirmedAtUtc,
            CancellationToken ct = default)
    {
        var user =
            await RequireUserAsync(
                identityId,
                ct);

        if (user.PhoneNumberConfirmed)
        {
            return IdentityOperationResult.Success();
        }

        user.PhoneNumberConfirmed =
            true;

        user.UpdatedAt =
            confirmedAtUtc;

        var result =
            await _userManager.UpdateAsync(
                user);

        return Map(result);
    }

    public async Task<IdentityOperationResult>
        UpdateSecurityStampAsync(
            Guid identityId,
            CancellationToken ct = default)
    {
        var user =
            await RequireUserAsync(
                identityId,
                ct);

        var result =
            await _userManager.UpdateSecurityStampAsync(
                user);

        return Map(result);
    }

    public async Task<IdentityOperationResult>
        RecordSuccessfulLoginAsync(
            Guid identityId,
            DateTime loginAtUtc,
            CancellationToken ct = default)
    {
        var user =
            await RequireUserAsync(
                identityId,
                ct);

        user.LastLoginAt =
            loginAtUtc;

        user.UpdatedAt =
            loginAtUtc;

        var result =
            await _userManager.UpdateAsync(
                user);

        return Map(result);
    }

    public async Task<string>
        GeneratePasswordResetTokenAsync(
            Guid identityId,
            CancellationToken ct = default)
    {
        var user =
            await RequireUserAsync(
                identityId,
                ct);

        return await _userManager
            .GeneratePasswordResetTokenAsync(
                user);
    }

    public async Task<IdentityOperationResult>
        ResetPasswordAsync(
            Guid identityId,
            string token,
            string newPassword,
            CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            token);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            newPassword);

        var user =
            await RequireUserAsync(
                identityId,
                ct);

        var result =
            await _userManager.ResetPasswordAsync(
                user,
                token,
                newPassword);

        return Map(result);
    }

    public async Task<IdentityOperationResult>
        ChangePasswordAsync(
            Guid identityId,
            string currentPassword,
            string newPassword,
            CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            currentPassword);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            newPassword);

        var user =
            await RequireUserAsync(
                identityId,
                ct);

        var result =
            await _userManager.ChangePasswordAsync(
                user,
                currentPassword,
                newPassword);

        return Map(result);
    }

    public async Task<IdentityOperationResult>
        RecordCredentialChangeAsync(
            Guid identityId,
            DateTime changedAtUtc,
            CancellationToken ct = default)
    {
        var user =
            await RequireUserAsync(
                identityId,
                ct);

        user.UpdatedAt =
            changedAtUtc;

        var result =
            await _userManager.UpdateAsync(
                user);

        return Map(result);
    }

    public async Task<IdentityOperationResult>
        SetEmailAsync(
            Guid identityId,
            string email,
            CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            email);

        var user =
            await RequireUserAsync(
                identityId,
                ct);

        var result =
            await _userManager.SetEmailAsync(
                user,
                email);

        return Map(result);
    }

    public async Task<string>
        GenerateEmailConfirmationTokenAsync(
            Guid identityId,
            CancellationToken ct = default)
    {
        var user =
            await RequireUserAsync(
                identityId,
                ct);

        return await _userManager
            .GenerateEmailConfirmationTokenAsync(
                user);
    }

    public async Task<IdentityOperationResult>
        ConfirmEmailAsync(
            Guid identityId,
            string token,
            CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            token);

        var user =
            await RequireUserAsync(
                identityId,
                ct);

        var result =
            await _userManager.ConfirmEmailAsync(
                user,
                token);

        return Map(result);
    }

    private async Task<ApplicationUser>
        RequireUserAsync(
            Guid identityId,
            CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return await _userManager.FindByIdAsync(
                   identityId.ToString())
               ?? throw new InvalidOperationException(
                   $"Identity user '{identityId}' was not found.");
    }

    private static IdentityAccountSnapshot Map(
        ApplicationUser user)
        => new(
            IdentityId:
                user.Id,

            UserAccountId:
                user.Id,

            Email:
                user.Email,

            PhoneNumber:
                user.PhoneNumber,

            EmailConfirmed:
                user.EmailConfirmed,

            PhoneConfirmed:
                user.PhoneNumberConfirmed,

            HasPassword:
                !string.IsNullOrWhiteSpace(
                    user.PasswordHash),

            IsDeleted:
                user.IsDeleted,

            UserName:
                user.UserName,

            SecurityStamp:
                user.SecurityStamp);

    private static IdentityOperationResult Map(
        IdentityResult result)
        => result.Succeeded
            ? IdentityOperationResult.Success()
            : IdentityOperationResult.Failed(
                result.Errors.Select(
                    error =>
                        error.Description));

    public async Task<IdentityOperationResult>
    SetPhoneNumberAsync(
        Guid identityId,
        string? phoneNumber,
        DateTime changedAtUtc,
        CancellationToken ct = default)
    {
        var user =
            await RequireUserAsync(
                identityId,
                ct);

        var normalizedPhone =
            string.IsNullOrWhiteSpace(phoneNumber)
                ? null
                : phoneNumber.Trim();

        if (string.Equals(
                user.PhoneNumber,
                normalizedPhone,
                StringComparison.Ordinal))
        {
            return IdentityOperationResult.Success();
        }

        user.PhoneNumber =
            normalizedPhone;

        user.PhoneNumberLookupHash =
            normalizedPhone is null
                ? null
                : _phoneLookupHasher.Compute(
                    normalizedPhone);

        /*
         * A changed phone number must be verified again.
         */
        user.PhoneNumberConfirmed =
            false;

        user.UpdatedAt =
            changedAtUtc;

        var result =
            await _userManager.UpdateAsync(
                user);

        return Map(result);
    }
    public async Task<IdentityOperationResult>
    SoftDeleteAsync(
        Guid identityId,
        DateTime deletedAtUtc,
        CancellationToken ct = default)
    {
        var user =
            await RequireUserAsync(
                identityId,
                ct);

        if (user.IsDeleted)
        {
            return IdentityOperationResult.Success();
        }

        user.IsDeleted =
            true;

        user.DeletedAt =
            deletedAtUtc;

        user.UpdatedAt =
            deletedAtUtc;

        var result =
            await _userManager.UpdateAsync(
                user);

        return Map(result);
    }
}
