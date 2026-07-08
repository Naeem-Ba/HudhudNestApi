using Microsoft.AspNetCore.Identity;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Identity.Entities;

/// <summary>
/// Authentication and account-security representation of a platform user.
///
/// Contains Identity credentials, verification state, administrative access state,
/// and the reference to the corresponding domain UserAccount.
///
/// Business profile data does not belong here.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// Foreign key to the domain/business profile.
    /// </summary>
    public Guid UserAccountId { get; set; }

    /// <summary>
    /// Administrative account ban.
    /// This is different from temporary ASP.NET Identity lockout.
    /// </summary>
    public bool IsBanned { get; set; }

    /// <summary>
    /// Administrative reason for banning the account.
    /// </summary>
    public string? BanReason { get; set; }

    /// <summary>
    /// Timestamp of the latest successful authentication.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Soft-delete flag for authentication access.
    /// A deleted identity must not be able to authenticate or refresh tokens.
    /// </summary>
    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public UserAccount UserAccount { get; set; } = null!;
}