namespace PropertyApi.Application.Users.DTOs;

/// <summary>
/// Data Transfer Object for user profile responses.
/// Never expose sensitive fields such as PasswordHash or SecurityStamp.
/// Authorization state is derived from Roles, not boolean flags.
/// </summary>
public sealed class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? ProfileImageUrl { get; set; }

    public string PreferredLanguage { get; set; } = "en";
    public string PreferredCurrency { get; set; } = "EUR";
    public string? CountryCode { get; set; }

    public string? Bio { get; set; }
    public string? ContactInfo { get; set; }

    public bool EmailConfirmed { get; set; }

    /// <summary>Not previously exposed here — added alongside PlanTier so a client can
    /// check "email confirmed OR phone confirmed" without a second call.</summary>
    public bool PhoneConfirmed { get; set; }

    /// <summary>
    /// null means the user has not explicitly chosen a plan yet — a distinct state
    /// from being on the free plan. See UserAccount.PlanId's doc comment.
    /// </summary>
    public string? PlanTier { get; set; }

    public DateTime? PlanSelectedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Minimal user summary used in public owner profile responses.
/// Agent status must be determined from Roles externally.
/// </summary>
public sealed class UserSummaryDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public string? PhoneNumber { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}

