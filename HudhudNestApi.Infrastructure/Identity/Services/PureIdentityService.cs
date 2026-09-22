using HudhudNestApi.Application.Auth.Models;

namespace HudhudNestApi.Infrastructure.Identity.Services;

/// <summary>
/// Temporary compatibility facade for the existing capability registrations.
/// It contains no Identity policy or persistence logic; new code must depend on
/// the narrow Application capability interfaces rather than this type.
/// </summary>
[Obsolete("Compatibility facade. Remove after capability registrations are migrated.")]
public sealed class PureIdentityService : IIdentityCapabilityAdapter
{
    private readonly IdentityAccountReader _reader;
    private readonly IdentityAccountCreator _creator;
    private readonly IdentityAccessService _access;
    private readonly IdentityCredentialService _credentials;

    public PureIdentityService(
        IdentityAccountReader reader,
        IdentityAccountCreator creator,
        IdentityAccessService access,
        IdentityCredentialService credentials)
    {
        _reader = reader;
        _creator = creator;
        _access = access;
        _credentials = credentials;
    }

    public Task<IdentityAccountSnapshot?> FindByIdAsync(Guid id, CancellationToken ct = default) => _reader.FindByIdAsync(id, ct);
    public Task<IdentityAccountSnapshot?> FindByEmailAsync(string email, CancellationToken ct = default) => _reader.FindByEmailAsync(email, ct);
    public Task<IdentityAccountSnapshot?> FindByLoginAsync(string provider, string key, CancellationToken ct = default) => _reader.FindByLoginAsync(provider, key, ct);
    public Task<IdentityAccountSnapshot?> FindByPhoneNumberAsync(string phone, CancellationToken ct = default) => _reader.FindByPhoneNumberAsync(phone, ct);
    public Task<IdentityOperationResult> CreateAsync(CreateIdentityAccount request, CancellationToken ct = default) => _creator.CreateAsync(request, ct);
    public Task<IdentityOperationResult> AddLoginAsync(Guid id, string provider, string key, string display, CancellationToken ct = default) => _access.AddLoginAsync(id, provider, key, display, ct);
    public Task<LoginPasswordVerificationResult> VerifyPasswordWithLockoutAsync(Guid id, string password, CancellationToken ct = default) =>
        _access.VerifyPasswordWithLockoutAsync(id, password, ct);
    public Task VerifyDummyPasswordAsync(CancellationToken ct = default) => _access.VerifyDummyPasswordAsync(ct);

#pragma warning disable CS0618
    [Obsolete("Use VerifyPasswordWithLockoutAsync instead to include account lockout protection.")]
    public Task<bool> CheckPasswordAsync(Guid id, string password, CancellationToken ct = default) => _access.CheckPasswordAsync(id, password, ct);
#pragma warning restore CS0618
    public Task<IReadOnlyList<string>> GetRolesAsync(Guid id, CancellationToken ct = default) => _access.GetRolesAsync(id, ct);
    public Task<IdentityOperationResult> AddToRoleAsync(Guid id, string role, CancellationToken ct = default) => _access.AddToRoleAsync(id, role, ct);
    public Task<IdentityOperationResult> RecordSuccessfulLoginAsync(Guid id, DateTime at, CancellationToken ct = default) => _access.RecordSuccessfulLoginAsync(id, at, ct);
    public Task<bool> IsLockedOutAsync(Guid id, CancellationToken ct = default) => _access.IsLockedOutAsync(id, ct);
    public Task<int> GetAccessFailedCountAsync(Guid id, CancellationToken ct = default) => _access.GetAccessFailedCountAsync(id, ct);
    public Task<DateTimeOffset?> GetLockoutEndAsync(Guid id, CancellationToken ct = default) => _access.GetLockoutEndAsync(id, ct);
    public Task<IdentityOperationResult> ConfirmPhoneNumberAsync(Guid id, DateTime at, CancellationToken ct = default) => _credentials.ConfirmPhoneNumberAsync(id, at, ct);
    public Task<IdentityOperationResult> UpdateSecurityStampAsync(Guid id, CancellationToken ct = default) => _credentials.UpdateSecurityStampAsync(id, ct);
    public Task<string> GeneratePasswordResetTokenAsync(Guid id, CancellationToken ct = default) => _credentials.GeneratePasswordResetTokenAsync(id, ct);
    public Task<IdentityOperationResult> ResetPasswordAsync(Guid id, string token, string password, CancellationToken ct = default) => _credentials.ResetPasswordAsync(id, token, password, ct);
    public Task<IdentityOperationResult> ChangePasswordAsync(Guid id, string current, string replacement, CancellationToken ct = default) => _credentials.ChangePasswordAsync(id, current, replacement, ct);
    public Task<IdentityOperationResult> RecordCredentialChangeAsync(Guid id, DateTime at, CancellationToken ct = default) => _credentials.RecordCredentialChangeAsync(id, at, ct);
    public Task<IdentityOperationResult> SetEmailAsync(Guid id, string email, CancellationToken ct = default) => _credentials.SetEmailAsync(id, email, ct);
    public Task<string> GenerateEmailConfirmationTokenAsync(Guid id, CancellationToken ct = default) => _credentials.GenerateEmailConfirmationTokenAsync(id, ct);
    public Task<IdentityOperationResult> ConfirmEmailAsync(Guid id, string token, CancellationToken ct = default) => _credentials.ConfirmEmailAsync(id, token, ct);
    public Task<IdentityOperationResult> SetPhoneNumberAsync(Guid id, string? phone, DateTime at, CancellationToken ct = default) => _credentials.SetPhoneNumberAsync(id, phone, at, ct);
    public Task<IdentityOperationResult> SoftDeleteAsync(Guid id, DateTime at, CancellationToken ct = default) => _credentials.SoftDeleteAsync(id, at, ct);
    public Task<IdentityOperationResult> AnonymizeCredentialsAsync(Guid id, string anonymizedEmail, DateTime utcNow, CancellationToken ct = default) => _credentials.AnonymizeCredentialsAsync(id, anonymizedEmail, utcNow, ct);
}
