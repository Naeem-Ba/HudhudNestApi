namespace WohnungenApi.Application.Users.DTOs;

/// <summary>
/// Data Transfer Object for user profile responses.
/// Never expose sensitive fields (PasswordHash, SecurityStamp, etc.).
/// </summary>
public sealed class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsAgent { get; set; }
    public string? ProfileImageUrl { get; set; }

    // Localization
    public string PreferredLanguage { get; set; } = "en";
    public string PreferredCurrency { get; set; } = "EUR";
    public string? CountryCode { get; set; }

    // Account meta
    public bool EmailConfirmed { get; set; }
    public DateTime CreatedAt { get; set; }

    // Roles assigned to this user (e.g. ["Admin", "Agent"])
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Minimal user summary used in property listings (owner info).
/// </summary>
public sealed class UserSummaryDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public bool IsAgent { get; set; }
    public string? PhoneNumber { get; set; }
}