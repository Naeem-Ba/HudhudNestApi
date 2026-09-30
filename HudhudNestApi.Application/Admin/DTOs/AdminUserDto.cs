namespace HudhudNestApi.Application.Admin.DTOs;

/// <summary>
/// One row of the admin user-management list — extended (admin dashboard: plan/ads
/// summary) beyond the original identity+roles shape so the list satisfies the spec's
/// minimum-columns requirement without a second round trip per row.
/// </summary>
public sealed class AdminUserDto
{
    public Guid Id { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public DateTime CreatedAt { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    /// <summary>"Active" | "Disabled" — derived from ApplicationUser.IsDeleted.</summary>
    public string AccountStatus { get; init; } = string.Empty;

    /// <summary>Null when the user has never selected a plan.</summary>
    public string? PlanTier { get; init; }

    /// <summary>"NoPlan" | "Active" | "Expired" | "Cancelled" — UserAccount.GetEffectivePlanStatus.</summary>
    public string PlanStatus { get; init; } = string.Empty;

    public DateTime? PlanExpiresAt { get; init; }

    public int TotalListings { get; init; }
    public int ActiveListings { get; init; }
    public int FeaturedListings { get; init; }
}
