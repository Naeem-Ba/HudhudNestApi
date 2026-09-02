namespace PropertyApi.Application.Admin.DTOs;

/// <summary>
/// Full detail behind AdminUserDto's list row — powers the admin user-detail page's
/// "الخطة والاشتراك" section. Superset of AdminUserDto rather than reusing it as a base
/// class: the two are shaped for different screens and shouldn't need to change together.
/// </summary>
public sealed class AdminUserDetailDto
{
    public Guid Id { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public DateTime CreatedAt { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public string AccountStatus { get; init; } = string.Empty;

    public string? PlanTier { get; init; }
    public string? PlanNameKey { get; init; }
    public string PlanStatus { get; init; } = string.Empty;
    public DateTime? PlanStartedAt { get; init; }
    public DateTime? PlanExpiresAt { get; init; }
    public DateTime? PlanCancelledAt { get; init; }

    /// <summary>"SelfService" | "AdminGrant" | null (never selected).</summary>
    public string? PlanActivationSource { get; init; }

    /// <summary>Null means unlimited (see Plan.ListingLimit's doc comment).</summary>
    public int? ListingLimit { get; init; }

    public int TotalListings { get; init; }
    public int ActiveListings { get; init; }
    public int FeaturedListings { get; init; }
}
