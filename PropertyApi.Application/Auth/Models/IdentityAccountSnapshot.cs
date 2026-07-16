namespace PropertyApi.Application.Auth.Models;

/// <summary>
/// Framework-neutral snapshot of identity/account-security state.
///
/// This model deliberately prevents Application handlers from
/// depending on ASP.NET Core Identity entities or Infrastructure types.
/// </summary>
public sealed record IdentityAccountSnapshot(
    Guid IdentityId,
    Guid UserAccountId,
    string? Email,
    string? PhoneNumber,
    bool EmailConfirmed,
    bool PhoneConfirmed,
    bool HasPassword,
    bool IsDeleted,
    string? UserName = null,
    string? SecurityStamp = null,
    DateTimeOffset? PhoneLastVerifiedAtUtc = null,
    DateTimeOffset? PhoneVerificationDueAtUtc = null,
    DateTimeOffset? PhoneVerificationGraceEndsAtUtc = null,
    PropertyApi.Domain.Enums.PhoneVerificationState PhoneVerificationState = PropertyApi.Domain.Enums.PhoneVerificationState.NotConfigured);

/// <summary>
/// Framework-neutral request for creating an identity account.
///
/// UserAccountId represents the corresponding business/domain profile.
/// During the current migration period, IdentityId and UserAccountId
/// use the same Guid value.
/// </summary>
public sealed record CreateIdentityAccount(
    Guid UserAccountId,
    string? Email,
    string? PhoneNumber,
    string? Password,
    string? LegacyFirstName = null,
    string? LegacyLastName = null,
    DateTime? LegacyCreatedAtUtc = null,
    bool EmailConfirmed = false,
    string? LegacyProfileImageUrl = null,
    bool PhoneConfirmed = false);
