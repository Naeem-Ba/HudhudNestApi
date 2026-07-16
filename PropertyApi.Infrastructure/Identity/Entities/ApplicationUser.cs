using Microsoft.AspNetCore.Identity;

using PropertyApi.Domain.Enums;

namespace PropertyApi.Infrastructure.Identity.Entities;

/// <summary>
/// Authentication and account-security representation
/// of a platform user.
///
/// Contains Identity credentials, verification state,
/// security state, administrative access state,
/// and authentication lifecycle timestamps.
///
/// Business profile data belongs to UserAccount.
/// </summary>
public sealed class ApplicationUser
    : IdentityUser<Guid>
{
    /// <summary>
    /// Deterministic HMAC lookup value used to find
    /// identities by encrypted phone number.
    /// </summary>
    public string? PhoneNumberLookupHash { get; set; }

    public string? NormalizedPhoneNumber { get; set; }

    public DateTimeOffset? PhoneLastVerifiedAtUtc { get; set; }

    public DateTimeOffset? PhoneVerificationDueAtUtc { get; set; }

    public DateTimeOffset? PhoneVerificationGraceEndsAtUtc { get; set; }

    public PhoneVerificationState PhoneVerificationState { get; set; }

    public string? LastPhoneVerificationNotificationKey { get; set; }

    /// <summary>
    /// Identity creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }
        = DateTime.UtcNow;

    /// <summary>
    /// Latest identity/security-state update timestamp.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
        = DateTime.UtcNow;

    /// <summary>
    /// Administrative account ban.
    /// Separate from ASP.NET Identity lockout.
    /// </summary>
    public bool IsBanned { get; set; }

    /// <summary>
    /// Administrative reason for banning the identity.
    /// </summary>
    public string? BanReason { get; set; }

    /// <summary>
    /// Timestamp of the latest successful authentication.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Soft-delete flag for authentication access.
    /// </summary>
    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }
}
